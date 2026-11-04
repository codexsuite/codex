using System.Text.Json;
using System.Threading.Channels;
using Codex.Application.Features.Print.Render;
using Codex.Application.Telemetry;
using Codex.Contracts.Events;
using Codex.Contracts.Storage;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Features.Print.Proof;

// Compiles queued proofs (export_job kind proof) in the process that has the print toolchain: the worker image, or a
// single process with CODEX_ROLE=all. It claims a job with FOR UPDATE SKIP LOCKED, assembles the chapter again with the
// job's template version (the one recorded on the row, even if the book has upgraded since), trim and options and the proxy images, compiles it with the render core, stores the PDF as
// an asset and settles the row. A failed conversion or compile keeps pandoc's and Tectonic's log on the row. A running
// job whose process stopped (started_at older than StaleAfter) is marked failed; the user's Re-render queues a new one.
// Every step is published as a JobProgress event.
public sealed class ProofWorker(
    NpgsqlDataSource dataSource,
    IRenderService renderer,
    IBlobStore blobs,
    IBookEvents events,
    ProofSignal signal,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<ProofWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private const int MaxLog = 200_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!CodexRole.RunsJobs(configuration))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool more;
                do
                {
                    more = await ProcessNextAsync(stoppingToken);
                }
                while (more);

                await signal.WaitAsync(PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Proof worker loop failed; retrying in {Seconds} s", PollInterval.TotalSeconds);
                await Task.Delay(PollInterval, clock, stoppingToken);
            }
        }
    }

    // Compiles the next queued proof. False when none was waiting.
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await ClaimAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        // One span and one pair of metrics per job that ended; the polling loop and an idle poll record nothing.
        using var span = JobSpan.Proof();

        // The role is read again when the job runs (#80): someone who asked for this proof and has since lost write
        // access to the book does not get it.
        if (!job.RequesterCanWrite)
        {
            span.Fail();
            await FailAsync(job, "The person who asked for this proof can no longer edit the book.", null);
            return true;
        }

        try
        {
            await CompileAsync(job, span, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            span.Abandon();
            await RequeueAsync(job);
            throw;
        }
        catch (Exception exception)
        {
            span.Fail();
            logger.LogWarning(exception, "Proof {JobId} failed", job.Id);
            await FailAsync(job, exception.Message, null);
        }

        return true;
    }

    private async Task<JobRow?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var stale = await connection.QueryAsync<(Guid Id, Guid BookId)>(new CommandDefinition(
            """
            UPDATE export_job SET status = 'failed', step = 'Failed', finished_at = @now,
                   error = 'The worker stopped before the proof finished. Re-render to try again.'
            WHERE kind = 'proof' AND status = 'running' AND started_at < @staleBefore
            RETURNING id, book_id
            """,
            new { now = now.UtcDateTime, staleBefore = (now - StaleAfter).UtcDateTime }, cancellationToken: cancellationToken));
        foreach (var (id, bookId) in stale)
        {
            await events.PublishAsync(new JobProgress(bookId, id, "proof", 100, "failed", "The worker stopped before the proof finished."));
        }

        return await connection.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition(
            """
            UPDATE export_job j SET status = 'running', step = 'Assembling', progress = 0, started_at = @now, finished_at = NULL, error = NULL
            FROM layout_template_version v JOIN layout_template t ON t.id = v.template_id, book b
            WHERE v.id = j.template_version_id AND b.id = j.book_id
              AND j.id = (SELECT id FROM export_job WHERE kind = 'proof' AND status = 'queued' ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING j.id, j.book_id AS BookId, j.chapter_id AS ChapterId, t.slug AS Template, j.template_version_id AS TemplateVersionId, j.trim_size AS Trim,
                      j.options::text AS Options, b.owner_id AS OwnerId,
                      (j.created_by IS NULL OR EXISTS (
                          SELECT 1 FROM book_access a WHERE a.book_id = j.book_id AND a.user_id = j.created_by AND a.role_rank >= 3)) AS RequesterCanWrite
            """,
            new { now = now.UtcDateTime }, cancellationToken: cancellationToken));
    }

    private async Task CompileAsync(JobRow job, JobSpan span, CancellationToken cancellationToken)
    {
        await events.PublishAsync(new JobProgress(job.BookId, job.Id, "proof", 0, "running", "Assembling"));
        using var stored = JsonDocument.Parse(job.Options);
        var options = stored.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        var request = new RenderRequest(job.BookId, RenderScope.Chapter, job.ChapterId, job.Template, job.Trim, options, "proxy", job.TemplateVersionId);

        var document = await renderer.AssembleAsync(request, RenderTarget.Pdf, cancellationToken);
        // The render core reports steps synchronously: queue them and let a pump task write them in order.
        var steps = Channel.CreateUnbounded<RenderProgress>();
        var pump = PumpAsync(job, steps.Reader);
        RenderedPdf pdf;
        try
        {
            pdf = await renderer.CompileAsync(document, new Reporter(steps.Writer), cancellationToken);
        }
        finally
        {
            steps.Writer.Complete();
            await pump;
        }

        using var _ = pdf;
        var log = Cap(pdf.Log);
        if (!pdf.Succeeded)
        {
            span.Fail();
            await FailAsync(job, pdf.Error!, log);
            return;
        }

        var bytes = await File.ReadAllBytesAsync(pdf.PdfPath!, cancellationToken);
        var key = $"proofs/{job.Id}.pdf";
        await blobs.PutAsync(key, new MemoryStream(bytes), "application/pdf", cancellationToken);
        var assetId = Guid.CreateVersion7();
        var now = clock.GetUtcNow().UtcDateTime;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO asset (id, owner_id, book_id, kind, filename, mime, bytes, storage_key, meta, created_at)
            VALUES (@assetId, @owner, @bookId, 'document', @filename, 'application/pdf', @bytes, @key, '{"generated":"proof"}'::jsonb, @now)
            """,
            new { assetId, owner = job.OwnerId, bookId = job.BookId, filename = $"proof-{job.Id:N}.pdf", bytes = (long)bytes.Length, key, now },
            transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE export_job SET status = 'succeeded', step = 'Done', progress = 1, content_hash = @hash, log = @log, error = NULL,
                   page_count = @pages, output_asset_id = @assetId, finished_at = @now
            WHERE id = @id
            """,
            new { id = job.Id, hash = document.ContentHash, log, pages = PdfPages.Count(bytes), assetId, now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        await events.PublishAsync(new JobProgress(job.BookId, job.Id, "proof", 100, "done", "Done"));
    }

    private async Task FailAsync(JobRow job, string error, string? log)
    {
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE export_job SET status = 'failed', step = 'Failed', progress = 1, error = @error, log = @log, finished_at = @now WHERE id = @id",
            new { id = job.Id, error, log, now = clock.GetUtcNow().UtcDateTime }));
        await events.PublishAsync(new JobProgress(job.BookId, job.Id, "proof", 100, "failed", error));
    }

    // The host is stopping mid-compile: the proof goes back to the queue for the next process.
    private async Task RequeueAsync(JobRow job)
    {
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE export_job SET status = 'queued', step = 'Queued', progress = 0, started_at = NULL WHERE id = @id AND status = 'running'",
            new { id = job.Id }));
    }

    private static string? Cap(string log) => log.Length <= MaxLog ? log : log[..MaxLog] + "\n… (log cut)";

    private async Task PumpAsync(JobRow job, ChannelReader<RenderProgress> steps)
    {
        await foreach (var progress in steps.ReadAllAsync())
        {
            await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE export_job SET step = @step, progress = @fraction WHERE id = @id AND status = 'running'",
                new { id = job.Id, step = progress.Step, fraction = progress.Fraction }));
            await events.PublishAsync(new JobProgress(job.BookId, job.Id, "proof", (int)(progress.Fraction * 100), "running", progress.Step));
        }
    }

    private sealed class Reporter(ChannelWriter<RenderProgress> writer) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => writer.TryWrite(value);
    }

    private sealed record JobRow(Guid Id, Guid BookId, Guid ChapterId, string Template, Guid TemplateVersionId, string Trim, string Options, Guid OwnerId, bool RequesterCanWrite);
}
