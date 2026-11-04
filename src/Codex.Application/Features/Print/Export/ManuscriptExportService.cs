using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Print;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Export;

// Export manuscript (J19): queues a Word, Markdown or EPUB copy as an export_job (kind docx, markdown or epub) that the
// export worker builds. Not gated on References: the dialog only says how many sources are at risk. One of each kind
// runs at a time per book (export_job_live_document_uq).
public interface IManuscriptExportService
{
    // The live chapters and the at-risk source count. 404 unknown book.
    Task<ManuscriptExportInfoDto> InfoAsync(Guid bookId, CancellationToken cancellationToken);

    // Queues an export. 404 unknown book or chapter; 400 a bad format, scope or a chapter scope without a chapter;
    // 422 blocked when the book has no chapters; 409 one of this kind is already queued or running.
    Task<ExportDto> RequestAsync(Guid bookId, ManuscriptExportRequest request, CancellationToken cancellationToken);

    // The book's manuscript exports, newest first. 404 unknown book.
    Task<ExportListDto> ListAsync(Guid bookId, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class ManuscriptExportService(
    NpgsqlDataSource dataSource,
    IBookAccess access,
    ICurrentUser currentUser,
    IBookEvents events,
    IExportService exports,
    ExportSignal signal,
    TimeProvider clock) : IManuscriptExportService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ManuscriptExportInfoDto> InfoAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var chapters = (await connection.QueryAsync<ChapterRow>(new CommandDefinition(
            """
            SELECT c.id, c.title, c.is_numbered AS IsNumbered
            FROM chapter c JOIN act a ON a.id = c.act_id
            WHERE c.book_id = @bookId AND c.archived_at IS NULL AND a.archived_at IS NULL
            ORDER BY a.position, a.id, c.position, c.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();
        var number = 0;
        var atRisk = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT risk_count FROM book_reference_summary WHERE book_id = @bookId", new { bookId }, cancellationToken: cancellationToken));
        return new ManuscriptExportInfoDto(
            chapters.Select(c => new ManuscriptChapterDto(c.Id, c.Title, c.IsNumbered ? ++number : null)).ToList(), atRisk ?? 0);
    }

    public async Task<ExportDto> RequestAsync(Guid bookId, ManuscriptExportRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        var options = ManuscriptOptions.From(request);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var live = await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT c.id FROM chapter c JOIN act a ON a.id = c.act_id
            WHERE c.book_id = @bookId AND c.archived_at IS NULL AND a.archived_at IS NULL
            """,
            new { bookId }, cancellationToken: cancellationToken));
        var chapterIds = live.ToList();
        if (chapterIds.Count == 0)
        {
            throw ApiException.Blocked(ManuscriptMarkdown.NothingToExport);
        }

        if (options.Scope == "chapter" && !chapterIds.Contains(options.ChapterId!.Value))
        {
            throw ApiException.NotFound($"Chapter {options.ChapterId} was not found in this book.");
        }

        Guid jobId;
        try
        {
            jobId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO export_job (book_id, kind, settings, status, step, progress, created_by, created_at)
                VALUES (@bookId, @kind, @settings::jsonb, 'queued', 'Queued', 0, @user, @now)
                RETURNING id
                """,
                new
                {
                    bookId,
                    kind = options.Format,
                    settings = JsonSerializer.Serialize(options, Json),
                    user = currentUser.Id,
                    now = clock.GetUtcNow().UtcDateTime,
                },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException e) when (e.ConstraintName == "export_job_live_document_uq")
        {
            throw ApiException.Conflict("An export of this kind is already running. Wait for it to finish.");
        }

        signal.Notify();
        await events.PublishAsync(new JobProgress(bookId, jobId, options.Format, 0, "queued", "Queued"));
        return await exports.GetAsync(jobId, cancellationToken);
    }

    public async Task<ExportListDto> ListAsync(Guid bookId, CancellationToken cancellationToken) =>
        await exports.ListAsync(bookId, ["docx", "markdown", "epub"], cancellationToken);

    private sealed record ChapterRow(Guid Id, string Title, bool IsNumbered);
}
