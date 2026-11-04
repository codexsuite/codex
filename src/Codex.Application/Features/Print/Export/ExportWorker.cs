using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Codex.Application.Features.Print.Proof;
using Codex.Application.Features.Print.Render;
using Codex.Application.Telemetry;
using Codex.Contracts.Events;
using Codex.Contracts.Storage;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Codex.Application.Features.Print.Export;

// Builds queued exports (export_job kind print, source, docx, markdown and epub) in the process that has the print toolchain: the worker
// image, or a single process with CODEX_ROLE=all. It claims a job with FOR UPDATE SKIP LOCKED. A print job assembles the
// whole book again with the job's template version, trim, options and image quality, compiles it with the render core
// (bleed, crop marks and the draft watermark applied by PrintFinish), stores the PDF as an asset and settles the row with
// the page count and the overfull boxes found in the log. A source job zips the book's Markdown and JSON (SourceArchive). A
// docx, markdown or epub job writes the manuscript for a reader (ManuscriptExporter) and stores what it left out as notes.
// Every step is a JobProgress event. A job cancelled while it ran is left cancelled and its file is dropped. A running
// job whose process stopped (started_at older than StaleAfter) is marked failed; Export again queues a new one.
public sealed partial class ExportWorker(
    NpgsqlDataSource dataSource,
    IRenderService renderer,
    ISourceArchive archive,
    IManuscriptExporter manuscripts,
    IBlobStore blobs,
    IBookEvents events,
    ExportSignal signal,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<ExportWorker> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(45);

    private const int MaxLog = 200_000;
    private const int MaxOverfull = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"^Overfull \\hbox \(([0-9.]+)pt too wide\).*$", RegexOptions.Multiline)]
    private static partial Regex OverfullLine();

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
                logger.LogError(exception, "Export worker loop failed; retrying in {Seconds} s", PollInterval.TotalSeconds);
                await Task.Delay(PollInterval, clock, stoppingToken);
            }
        }
    }

    // Builds the next queued export. False when none was waiting.
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var job = await ClaimAsync(cancellationToken);
        if (job is null)
        {
            return false;
        }

        // One span and one pair of metrics per job that ended; the polling loop and an idle poll record nothing.
        using var span = JobSpan.Export();

        // The role is read again when the job runs (#80): someone who asked for this export and has since lost write
        // access to the book does not get it.
        if (!job.RequesterCanWrite)
        {
            span.Fail();
            await FailAsync(job, "The person who asked for this export can no longer edit the book.", null);
            return true;
        }

        try
        {
            if (job.Kind == "source")
            {
                await BuildSourceAsync(job, cancellationToken);
            }
            else if (job.Kind is "docx" or "markdown" or "epub")
            {
                await BuildManuscriptAsync(job, cancellationToken);
            }
            else
            {
                await BuildPrintAsync(job, span, cancellationToken);
            }
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
            logger.LogWarning(exception, "Export {JobId} failed", job.Id);
            await FailAsync(job, exception.Message, null);
        }

        return true;
    }

    private async Task<JobRow?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var stale = await connection.QueryAsync<(Guid Id, Guid BookId, string Kind)>(new CommandDefinition(
            """
            UPDATE export_job SET status = 'failed', step = 'Failed', finished_at = @now,
                   error = 'The worker stopped before the export finished. Export again to try once more.'
            WHERE kind IN ('print', 'source', 'docx', 'markdown', 'epub') AND status = 'running' AND started_at < @staleBefore
            RETURNING id, book_id, kind
            """,
            new { now = now.UtcDateTime, staleBefore = (now - StaleAfter).UtcDateTime }, cancellationToken: cancellationToken));
        foreach (var (id, bookId, kind) in stale)
        {
            await events.PublishAsync(new JobProgress(bookId, id, kind, 100, "failed", "The worker stopped before the export finished."));
        }

        return await connection.QuerySingleOrDefaultAsync<JobRow>(new CommandDefinition(
            """
            UPDATE export_job j SET status = 'running', step = 'Assembling', progress = 0, started_at = @now, finished_at = NULL, error = NULL
            FROM book b
            WHERE b.id = j.book_id
              AND j.id = (SELECT id FROM export_job WHERE kind IN ('print', 'source', 'docx', 'markdown', 'epub') AND status = 'queued' ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED)
            RETURNING j.id, j.book_id AS BookId, j.kind, j.trim_size AS Trim, j.options::text AS Options, j.settings::text AS Settings,
                      (SELECT t.slug FROM layout_template_version v JOIN layout_template t ON t.id = v.template_id WHERE v.id = j.template_version_id) AS Template,
                      j.template_version_id AS TemplateVersionId,
                      b.owner_id AS OwnerId, (j.created_by IS NULL OR EXISTS (
                          SELECT 1 FROM book_access a WHERE a.book_id = j.book_id AND a.user_id = j.created_by AND a.role_rank >= 3)) AS RequesterCanWrite
            """,
            new { now = now.UtcDateTime }, cancellationToken: cancellationToken));
    }

    private async Task BuildSourceAsync(JobRow job, CancellationToken cancellationToken)
    {
        await events.PublishAsync(new JobProgress(job.BookId, job.Id, job.Kind, 0, "running", "Collecting the manuscript"));
        var built = await archive.BuildAsync(job.BookId, cancellationToken);
        await StoreAsync(
            job, built.Bytes, "application/zip", $"source-{job.Id:N}.zip", $"exports/{job.Id}.zip", "source", null, null,
            JsonSerializer.Serialize(new { notes = built.Notes }, Json), cancellationToken);
    }

    private async Task BuildManuscriptAsync(JobRow job, CancellationToken cancellationToken)
    {
        await events.PublishAsync(new JobProgress(job.BookId, job.Id, job.Kind, 0, "running", "Collecting the manuscript"));
        var built = await manuscripts.BuildAsync(job.BookId, job.Settings, cancellationToken);
        await StoreAsync(
            job, built.Bytes, built.Mime, built.Filename, $"exports/{job.Id}{Path.GetExtension(built.Filename)}", "manuscript", null, null,
            JsonSerializer.Serialize(new { notes = built.Notes }, Json), cancellationToken);
    }

    private async Task BuildPrintAsync(JobRow job, JobSpan span, CancellationToken cancellationToken)
    {
        await events.PublishAsync(new JobProgress(job.BookId, job.Id, job.Kind, 0, "running", "Assembling"));
        using var stored = JsonDocument.Parse(job.Options);
        var options = stored.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        var settings = JsonSerializer.Deserialize<ExportSettings>(job.Settings, Json)!;
        var request = new RenderRequest(job.BookId, RenderScope.Book, null, job.Template, job.Trim, options, settings.ImagePurpose, job.TemplateVersionId);

        var document = await renderer.AssembleAsync(request, RenderTarget.Pdf, cancellationToken);
        var finish = new PrintFinish(settings.BleedInches, settings.CropMarks, settings.Draft);
        // The render core reports steps synchronously: queue them and let a pump task write them in order.
        var steps = Channel.CreateUnbounded<RenderProgress>();
        var pump = PumpAsync(job, steps.Reader);
        RenderedPdf pdf;
        try
        {
            pdf = await renderer.CompileAsync(document, new Reporter(steps.Writer), cancellationToken, finish);
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
        var overfull = OverfullLine().Matches(pdf.Log).Select(m => m.Value.Trim()).Take(MaxOverfull).ToList();
        await StoreAsync(
            job, bytes, "application/pdf", $"export-{job.Id:N}.pdf", $"exports/{job.Id}.pdf", "export", document.ContentHash, log,
            JsonSerializer.Serialize(new { overfull }, Json), cancellationToken, PdfPages.Count(bytes));
    }

    // Stores the file as an asset and settles the row, unless the job was cancelled meanwhile (then the file is dropped).
    private async Task StoreAsync(
        JobRow job, byte[] bytes, string mime, string filename, string key, string generated, string? hash, string? log, string? diagnostics,
        CancellationToken cancellationToken, int? pages = null)
    {
        await blobs.PutAsync(key, new MemoryStream(bytes), mime, cancellationToken);
        var assetId = Guid.CreateVersion7();
        var now = clock.GetUtcNow().UtcDateTime;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO asset (id, owner_id, book_id, kind, filename, mime, bytes, storage_key, meta, created_at)
            VALUES (@assetId, @owner, @bookId, 'document', @filename, @mime, @size, @key, jsonb_build_object('generated', @generated::text), @now)
            """,
            new { assetId, owner = job.OwnerId, bookId = job.BookId, filename, mime, size = (long)bytes.Length, key, generated, now },
            transaction, cancellationToken: cancellationToken));
        var settled = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE export_job SET status = 'succeeded', step = 'Done', progress = 1, content_hash = @hash, log = @log, error = NULL,
                   diagnostics = @diagnostics::jsonb, page_count = @pages, output_asset_id = @assetId, finished_at = @now
            WHERE id = @id AND status = 'running'
            """,
            new { id = job.Id, hash, log, diagnostics, pages, assetId, now }, transaction, cancellationToken: cancellationToken));
        if (settled == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await blobs.DeleteAsync(key, cancellationToken);
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        await events.PublishAsync(new JobProgress(job.BookId, job.Id, job.Kind, 100, "done", "Done"));
    }

    private async Task FailAsync(JobRow job, string error, string? log)
    {
        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        var failed = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE export_job SET status = 'failed', step = 'Failed', progress = 1, error = @error, log = @log, finished_at = @now WHERE id = @id AND status = 'running'",
            new { id = job.Id, error, log, now = clock.GetUtcNow().UtcDateTime }));
        if (failed > 0)
        {
            await events.PublishAsync(new JobProgress(job.BookId, job.Id, job.Kind, 100, "failed", error));
        }
    }

    // The host is stopping mid-build: the export goes back to the queue for the next process.
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
            await events.PublishAsync(new JobProgress(job.BookId, job.Id, job.Kind, (int)(progress.Fraction * 100), "running", progress.Step));
        }
    }

    private sealed class Reporter(ChannelWriter<RenderProgress> writer) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => writer.TryWrite(value);
    }

    private sealed record JobRow(Guid Id, Guid BookId, string Kind, string? Trim, string Options, string Settings, string? Template, Guid? TemplateVersionId, Guid OwnerId, bool RequesterCanWrite);
}
