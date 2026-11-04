using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Print;
using Codex.Contracts.Storage;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Print.Export;

// Export for printer (L7.5): the whole book as a print PDF, built by the worker. Requesting one runs preflight, then
// writes an export_job row (kind print) with the settings and the preflight snapshot and wakes the worker; the web
// image has no TeX toolchain. A source export (kind source) is queued from Settings and built by the same worker.
// Plain application commands, so the MCP print tools can adapt them.
public interface IExportService
{
    // Preflight for the settings in the request, without queuing anything. 404 unknown book; 400 a bad setting, trim or option.
    Task<PreflightDto> PreflightAsync(Guid bookId, ExportRequest request, CancellationToken cancellationToken);

    // Queues an export. 404 unknown book; 400 a bad setting; 422 blocked while preflight has Blocks and the request is not a
    // draft; 409 an export of the book is already queued or running.
    Task<ExportDto> RequestAsync(Guid bookId, ExportRequest request, CancellationToken cancellationToken);

    // The book's exports (print and source), newest first. 404 unknown book.
    Task<ExportListDto> ListAsync(Guid bookId, CancellationToken cancellationToken);

    // The book's exports of the given kinds (manuscript copies: docx, markdown, epub; or source), newest first. 404 unknown book.
    Task<ExportListDto> ListAsync(Guid bookId, IReadOnlyList<string> kinds, CancellationToken cancellationToken);

    // One export with its log. 404 unknown export.
    Task<ExportDto> GetAsync(Guid exportId, CancellationToken cancellationToken);

    // Cancels a queued or running export. 404 unknown; 409 already finished.
    Task<ExportDto> CancelAsync(Guid exportId, CancellationToken cancellationToken);

    // The finished file (PDF or source .zip). 404 unknown export or missing file; 409 not finished (or it failed).
    Task<ExportFile> OpenFileAsync(Guid exportId, CancellationToken cancellationToken);
}

public sealed record ExportFile(BlobContent Content, string Filename);

[RegisterSingleton]
public sealed class ExportService(
    NpgsqlDataSource dataSource,
    IExportPreflight preflight,
    IBookAccess access,
    IBlobStore blobs,
    ICurrentUser currentUser,
    IBookEvents events,
    ExportSignal signal,
    TimeProvider clock) : IExportService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PreflightDto> PreflightAsync(Guid bookId, ExportRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        return (await preflight.PlanAsync(bookId, request, cancellationToken)).Preflight;
    }

    public async Task<ExportDto> RequestAsync(Guid bookId, ExportRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        var plan = await preflight.PlanAsync(bookId, request, cancellationToken);
        if (plan.Preflight.Blocks.Count > 0 && !plan.Settings.Draft)
        {
            var n = plan.Preflight.Blocks.Count;
            throw ApiException.Blocked($"{n} {(n == 1 ? "issue blocks" : "issues block")} export. Fix {(n == 1 ? "it" : "them")} or export a draft.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var running = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM export_job WHERE book_id = @bookId AND kind = 'print' AND status IN ('queued', 'running'))",
            new { bookId }, cancellationToken: cancellationToken));
        if (running)
        {
            throw ApiException.Conflict("An export of this book is already running. Cancel it or wait for it to finish.");
        }

        var jobId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO export_job (book_id, kind, template_version_id, trim_size, options, settings, preflight, status, step, progress, created_by, created_at)
            VALUES (@bookId, 'print', @versionId, @trim, @options::jsonb, @settings::jsonb, @preflight::jsonb, 'queued', 'Queued', 0, @user, @now)
            RETURNING id
            """,
            new
            {
                bookId,
                versionId = plan.TemplateVersionId,
                trim = plan.Document.Trim,
                options = JsonSerializer.Serialize(plan.Document.Options),
                settings = JsonSerializer.Serialize(plan.Settings, Json),
                preflight = JsonSerializer.Serialize(plan.Preflight, Json),
                user = currentUser.Id,
                now = clock.GetUtcNow().UtcDateTime,
            },
            cancellationToken: cancellationToken));
        signal.Notify();
        await events.PublishAsync(new JobProgress(bookId, jobId, "print", 0, "queued", "Queued"));
        return await GetAsync(jobId, cancellationToken);
    }

    public Task<ExportListDto> ListAsync(Guid bookId, CancellationToken cancellationToken) =>
        ListAsync(bookId, ["print", "source"], cancellationToken);

    public async Task<ExportListDto> ListAsync(Guid bookId, IReadOnlyList<string> kinds, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }

        var rows = (await connection.QueryAsync<ExportRow>(new CommandDefinition(
            Select + " WHERE j.book_id = @bookId AND j.kind = ANY(@kinds) ORDER BY j.created_at DESC, j.id DESC LIMIT 30",
            new { bookId, kinds = kinds.ToArray() }, cancellationToken: cancellationToken))).ToList();
        var active = rows.FirstOrDefault(r => r.Kind == "print" && r.Status is "queued" or "running");
        return new ExportListDto(active?.Id, rows.Select(r => r.ToDto(false, clock)).ToList());
    }

    public async Task<ExportDto> GetAsync(Guid exportId, CancellationToken cancellationToken)
    {
        await RequireOwnedExportAsync(exportId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ExportRow>(new CommandDefinition(
            Select + " WHERE j.id = @exportId AND j.kind IN ('print', 'source', 'docx', 'markdown', 'epub')", new { exportId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Export {exportId} was not found.");
        return row.ToDto(true, clock);
    }

    public async Task<ExportDto> CancelAsync(Guid exportId, CancellationToken cancellationToken)
    {
        await RequireOwnedExportAsync(exportId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var cancelled = await connection.QuerySingleOrDefaultAsync<(Guid BookId, string Kind)?>(new CommandDefinition(
            """
            UPDATE export_job SET status = 'cancelled', step = 'Cancelled', finished_at = @now
            WHERE id = @exportId AND kind IN ('print', 'source', 'docx', 'markdown', 'epub') AND status IN ('queued', 'running')
            RETURNING book_id AS BookId, kind AS Kind
            """,
            new { exportId, now = clock.GetUtcNow().UtcDateTime }, cancellationToken: cancellationToken));
        if (cancelled is null)
        {
            var current = await GetAsync(exportId, cancellationToken);
            throw ApiException.Conflict($"The export is already {current.Status}.");
        }

        await events.PublishAsync(new JobProgress(cancelled.Value.BookId, exportId, cancelled.Value.Kind, 100, "failed", "Cancelled"));
        return await GetAsync(exportId, cancellationToken);
    }

    public async Task<ExportFile> OpenFileAsync(Guid exportId, CancellationToken cancellationToken)
    {
        await RequireOwnedExportAsync(exportId, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var file = await connection.QuerySingleOrDefaultAsync<(string Status, string? StorageKey, string? Filename)?>(new CommandDefinition(
            """
            SELECT j.status AS Status, a.storage_key AS StorageKey, a.filename AS Filename
            FROM export_job j LEFT JOIN asset a ON a.id = j.output_asset_id
            WHERE j.id = @exportId AND j.kind IN ('print', 'source', 'docx', 'markdown', 'epub')
            """,
            new { exportId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Export {exportId} was not found.");
        if (file.StorageKey is null)
        {
            throw ApiException.Conflict($"Export {exportId} has no file: it is {file.Status}.");
        }

        var content = await blobs.GetAsync(file.StorageKey, cancellationToken)
            ?? throw ApiException.NotFound($"The file of export {exportId} is missing from storage.");
        return new ExportFile(content, file.Filename ?? $"export-{exportId:N}");
    }

    private Task RequireOwnedExportAsync(Guid exportId, CancellationToken cancellationToken)
        => access.RequireAsync(BookItem.ExportJob, exportId, BookRole.Write, cancellationToken, $"Export {exportId} was not found.");

    private const string Select =
        """
        SELECT j.id, j.kind, j.status, j.step, j.progress, t.slug AS Template, v.version AS TemplateVersion, j.trim_size AS Trim,
               j.settings::text AS Settings, j.page_count AS PageCount, a.bytes AS Bytes, j.error, j.log,
               coalesce(jsonb_array_length(j.preflight -> 'blocks'), 0) AS Blocked,
               coalesce(jsonb_array_length(j.preflight -> 'warnings'), 0) AS Warnings,
               j.diagnostics::text AS Diagnostics, j.output_asset_id IS NOT NULL AS HasFile,
               j.created_at AS CreatedAt, j.finished_at AS FinishedAt, a.filename AS Filename
        FROM export_job j
        LEFT JOIN layout_template_version v ON v.id = j.template_version_id
        LEFT JOIN layout_template t ON t.id = v.template_id
        LEFT JOIN asset a ON a.id = j.output_asset_id
        """;

    private sealed record ExportRow(
        Guid Id, string Kind, string Status, string? Step, float? Progress, string? Template, string? TemplateVersion, string? Trim,
        string Settings, int? PageCount, long? Bytes, string? Error, string? Log, int Blocked, int Warnings, string? Diagnostics,
        bool HasFile, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, string? Filename)
    {
        public ExportDto ToDto(bool withLog, TimeProvider clock)
        {
            var settings = JsonSerializer.Deserialize<ExportSettings>(Settings, Json);
            var overfull = new List<string>();
            var notes = new List<string>();
            if (Diagnostics is not null)
            {
                using var document = JsonDocument.Parse(Diagnostics);
                if (document.RootElement.TryGetProperty("overfull", out var lines))
                {
                    overfull.AddRange(lines.EnumerateArray().Select(l => l.GetString() ?? string.Empty));
                }

                if (document.RootElement.TryGetProperty("notes", out var left))
                {
                    notes.AddRange(left.EnumerateArray().Select(l => l.GetString() ?? string.Empty));
                }
            }

            return new ExportDto(
                Id, Kind, Status, Step, Progress, Template, TemplateVersion, Trim,
                settings?.Bleed, settings?.CropMarks ?? false, settings?.Quality, settings?.Draft ?? false,
                PageCount, Bytes, Error, withLog ? Log : null, Blocked, Warnings, overfull,
                HasFile ? $"/api/exports/{Id}/file" : null, CreatedAt, DisplayDate.Relative(CreatedAt, clock), FinishedAt, Filename, notes);
        }
    }
}
