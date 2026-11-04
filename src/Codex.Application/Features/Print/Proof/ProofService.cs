using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Application.Features.Print.Render;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Print;
using Codex.Contracts.Storage;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Proof;

// Proofs (L7.4): a one-chapter PDF compiled with the real template and the 150 dpi proxy images. Requesting one only
// writes an export_job row (kind proof) and wakes the worker; the web image has no TeX toolchain, so the proof is
// compiled by ProofWorker in the worker process. The job's cache_key (content hash, template version, trim, options)
// makes a second request for unchanged content answer with the finished proof instead of compiling again.
// Plain application commands, so MCP tools can call them.
public interface IProofService
{
    // Queues a proof of the chapter, or answers with the cached (or already queued or running) proof of the same
    // content, template version, trim and options (Cached true) unless Rerender is set. 404 unknown book or chapter;
    // 400 a bad trim or option, or a template that cannot compile.
    Task<ProofDto> RequestAsync(Guid bookId, ProofRequest request, CancellationToken cancellationToken);

    // The chapter's proofs, newest first (at most 20), and the cache key a proof made now would have. 404 unknown book or chapter.
    Task<ProofListDto> ListAsync(Guid bookId, Guid chapterId, CancellationToken cancellationToken);

    // One proof with its log. 404 unknown proof.
    Task<ProofDto> GetAsync(Guid proofId, CancellationToken cancellationToken);

    // The proof's PDF. 404 unknown proof; 409 not finished (or it failed).
    Task<BlobContent> OpenPdfAsync(Guid proofId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class ProofService(
    NpgsqlDataSource dataSource,
    IRenderService renderer,
    IBookAccess access,
    IBlobStore blobs,
    ICurrentUser currentUser,
    IBookEvents events,
    ProofSignal signal,
    TimeProvider clock) : IProofService
{
    public async Task<ProofDto> RequestAsync(Guid bookId, ProofRequest request, CancellationToken cancellationToken)
    {
        var (document, versionId, key) = await CurrentAsync(bookId, request.ChapterId, request.Template, request.Trim, request.Options, cancellationToken);
        var options = JsonSerializer.Serialize(document.Options);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        if (!request.Rerender)
        {
            var reused = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                SELECT id FROM export_job
                WHERE book_id = @bookId AND kind = 'proof' AND chapter_id = @chapterId AND cache_key = @key
                  AND status IN ('queued', 'running', 'succeeded')
                ORDER BY created_at DESC, id DESC LIMIT 1
                """,
                new { bookId, request.ChapterId, key }, cancellationToken: cancellationToken));
            if (reused is { } id)
            {
                return (await GetAsync(id, cancellationToken)) with { Cached = true };
            }
        }

        var jobId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO export_job (book_id, kind, chapter_id, template_version_id, trim_size, options, content_hash, status, step, progress, created_by, created_at)
            VALUES (@bookId, 'proof', @chapterId, @versionId, @trim, @options::jsonb, @hash, 'queued', 'Queued', 0, @user, @now)
            RETURNING id
            """,
            new { bookId, request.ChapterId, versionId, trim = document.Trim, options, hash = document.ContentHash, user = currentUser.Id, now = clock.GetUtcNow().UtcDateTime },
            cancellationToken: cancellationToken));
        signal.Notify();
        await events.PublishAsync(new JobProgress(bookId, jobId, "proof", 0, "queued", "Queued"));
        return await GetAsync(jobId, cancellationToken);
    }

    public async Task<ProofListDto> ListAsync(Guid bookId, Guid chapterId, CancellationToken cancellationToken)
    {
        var (_, _, key) = await CurrentAsync(bookId, chapterId, null, null, null, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProofRow>(new CommandDefinition(
            Select + " WHERE j.book_id = @bookId AND j.kind = 'proof' AND j.chapter_id = @chapterId ORDER BY j.created_at DESC, j.id DESC LIMIT 20",
            new { bookId, chapterId }, cancellationToken: cancellationToken));
        return new ProofListDto(key, rows.Select(r => r.ToDto(false)).ToList());
    }

    public async Task<ProofDto> GetAsync(Guid proofId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.ExportJob, proofId, BookRole.Write, cancellationToken, $"Proof {proofId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProofRow>(new CommandDefinition(
            Select + " WHERE j.id = @proofId AND j.kind = 'proof'", new { proofId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Proof {proofId} was not found.");
        return row.ToDto(true);
    }

    public async Task<BlobContent> OpenPdfAsync(Guid proofId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.ExportJob, proofId, BookRole.Write, cancellationToken, $"Proof {proofId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var file = await connection.QuerySingleOrDefaultAsync<(string Status, string? StorageKey)?>(new CommandDefinition(
            """
            SELECT j.status AS Status, a.storage_key AS StorageKey
            FROM export_job j LEFT JOIN asset a ON a.id = j.output_asset_id
            WHERE j.id = @proofId AND j.kind = 'proof'
            """,
            new { proofId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Proof {proofId} was not found.");
        if (file.StorageKey is null)
        {
            throw ApiException.Conflict($"Proof {proofId} has no PDF: it is {file.Status}.");
        }

        return await blobs.GetAsync(file.StorageKey, cancellationToken)
            ?? throw ApiException.NotFound($"The PDF of proof {proofId} is missing from storage.");
    }

    // The chapter as it is now: its assembled document (for the content hash), the template version it renders with and
    // the cache key export_job would compute for them (the same expression as the column's).
    private async Task<(AssembledDocument Document, Guid VersionId, string Key)> CurrentAsync(
        Guid bookId, Guid chapterId, string? template, string? trim, IReadOnlyDictionary<string, JsonElement>? options, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var book = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: cancellationToken));
        if (!book)
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }

        var chapter = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM chapter WHERE id = @chapterId AND book_id = @bookId AND archived_at IS NULL)", new { bookId, chapterId }, cancellationToken: cancellationToken));
        if (!chapter)
        {
            throw ApiException.NotFound($"Chapter {chapterId} was not found in this book.");
        }

        var document = await renderer.AssembleAsync(
            new RenderRequest(bookId, RenderScope.Chapter, chapterId, template, trim, options, "proxy"), RenderTarget.Pdf, cancellationToken);
        // The version the book pinned for the template (or the request's template): what the proof compiles with.
        var versionId = document.TemplateVersionId;
        var key = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT md5(coalesce(@hash, '') || '|' || @versionId::text || '|' || @trim || '|' || @options::jsonb::text)",
            new { hash = document.ContentHash, versionId, trim = document.Trim, options = JsonSerializer.Serialize(document.Options) }, cancellationToken: cancellationToken));
        return (document, versionId, key!);
    }

    private const string Select =
        """
        SELECT j.id, j.chapter_id AS ChapterId, j.status, j.step, j.progress, j.cache_key AS CacheKey, t.slug AS TemplateSlug,
               v.version AS TemplateVersion, j.trim_size AS Trim, j.page_count AS PageCount, j.error, j.log,
               j.output_asset_id IS NOT NULL AS HasPdf, j.created_at AS CreatedAt, j.finished_at AS FinishedAt
        FROM export_job j
        JOIN layout_template_version v ON v.id = j.template_version_id
        JOIN layout_template t ON t.id = v.template_id
        """;

    private sealed record ProofRow(
        Guid Id, Guid ChapterId, string Status, string? Step, float? Progress, string CacheKey, string TemplateSlug, string TemplateVersion,
        string Trim, int? PageCount, string? Error, string? Log, bool HasPdf, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt)
    {
        public ProofDto ToDto(bool withLog) => new(
            Id, ChapterId, Status, Step, Progress, CacheKey, TemplateSlug, TemplateVersion, Trim, PageCount, Error, withLog ? Log : null,
            false, HasPdf ? $"/api/proofs/{Id}/pdf" : null, CreatedAt, FinishedAt);
    }
}
