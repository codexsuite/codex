using System.Globalization;
using System.Text;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

[RegisterSingleton]
public sealed class SourceQueries(NpgsqlDataSource dataSource, IBookAccess access, TimeProvider clock) : ISourceQueries
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public async Task<IReadOnlyList<SourceRowDto>> GetSourcesAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await RequireBookAsync(connection, bookId, cancellationToken);
        var rows = await connection.QueryAsync<SourceRow>(new CommandDefinition(
            """
            SELECT r.id AS Id, c.number::integer AS Number, r.title AS Title, r.authors::text AS AuthorsJson,
                   r.year AS Year, r.publisher AS Publisher, r.kind AS Kind, k.label AS KindLabel,
                   l.label AS LicenceLabel, c.status AS Status, c.reason AS Reason, c.use_count AS Uses,
                   to_json(st.chapter_numbers)::text AS ChapterNumbersJson, c.quoted_words AS QuotedWords,
                   ps.quote_word_limit AS Limit, r.signed_off_at AS SignedOffAt, u.display_name AS SignedOffBy,
                   p.remind_at AS RemindAt, (p.remind_at <= now()) AS RemindDue
            FROM reference r
            JOIN reference_clearance c ON c.reference_id = r.id
            JOIN reference_stats st ON st.reference_id = r.id
            JOIN reference_kind k ON k.code = r.kind
            JOIN licence l ON l.code = r.licence
            JOIN book_publication_settings ps ON ps.book_id = r.book_id
            LEFT JOIN app_user u ON u.id = r.signed_off_by
            LEFT JOIN LATERAL (
                SELECT q.remind_at FROM permission_request q
                WHERE q.reference_id = r.id AND q.status = 'sent'
                ORDER BY q.created_at DESC LIMIT 1
            ) p ON true
            WHERE r.book_id = @bookId
            ORDER BY c.number NULLS LAST, lower(r.title), r.id
            """,
            new { bookId }, cancellationToken: cancellationToken));
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<SourceCslDto>> GetCslAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await RequireBookAsync(connection, bookId, cancellationToken);
        var rows = await QueryCslAsync(connection, bookId, citedOnly: true, cancellationToken);
        return rows.Select(r => new SourceCslDto(r.SourceId, r.Status, ParseCsl(r.Csl), r.CreditLine)).ToList();
    }

    public async Task<SourceExportFile> ExportAsync(Guid bookId, string? format, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var title = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT title FROM book WHERE id = @bookId AND kind = 'book'", new { bookId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Book {bookId} was not found.");
        var name = Slug(title) + "-sources";
        switch (format)
        {
            case "bibtex":
                return Text($"{name}.bib", "application/x-bibtex", BibTexWriter.Write(await AllCslAsync(connection, bookId, cancellationToken)));
            case "ris":
                return Text($"{name}.ris", "application/x-research-info-systems", RisWriter.Write(await AllCslAsync(connection, bookId, cancellationToken)));
            case "csljson":
                var items = await AllCslAsync(connection, bookId, cancellationToken);
                return Text($"{name}.json", "application/vnd.citationstyles.csl+json", JsonSerializer.Serialize(items, Indented));
            case "permissions":
                return Text($"{name}-permissions.csv", "text/csv", PermissionsLogWriter.Write(await PermissionsLogAsync(connection, bookId, cancellationToken)));
            default:
                throw ApiException.Validation($"Export format '{format}' is not supported. Use bibtex, ris, csljson or permissions.");
        }
    }

    private static async Task<IReadOnlyList<JsonElement>> AllCslAsync(NpgsqlConnection connection, Guid bookId, CancellationToken ct) =>
        (await QueryCslAsync(connection, bookId, citedOnly: false, ct)).Select(r => ParseCsl(r.Csl)).ToList();

    private static async Task<IReadOnlyList<CslRow>> QueryCslAsync(NpgsqlConnection connection, Guid bookId, bool citedOnly, CancellationToken ct) =>
        (await connection.QueryAsync<CslRow>(new CommandDefinition(
            """
            SELECT x.reference_id AS SourceId, c.status AS Status, x.csl::text AS Csl, x.credit_line AS CreditLine
            FROM reference_csl x
            JOIN reference_clearance c ON c.reference_id = x.reference_id
            WHERE x.book_id = @bookId AND (NOT @citedOnly OR c.use_count > 0)
            ORDER BY c.number NULLS LAST, lower(x.csl ->> 'title'), x.reference_id
            """,
            new { bookId, citedOnly }, cancellationToken: ct))).ToList();

    private async Task<IReadOnlyList<PermissionsLogRow>> PermissionsLogAsync(NpgsqlConnection connection, Guid bookId, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<PermissionRow>(new CommandDefinition(
            """
            SELECT r.title AS Title, r.authors::text AS AuthorsJson, r.year AS Year, k.label AS Kind,
                   l.label AS Licence, r.rights_holder AS RightsHolder, r.permissions_contact AS PermissionsContact,
                   c.use_count AS Uses, c.quoted_words AS QuotedWords, c.status AS Status, c.reason AS Reason,
                   p.status AS PermissionStatus, p.sent_at AS SentAt, p.credit_line AS CreditLine, p.terms AS Terms
            FROM reference r
            JOIN reference_clearance c ON c.reference_id = r.id
            JOIN reference_kind k ON k.code = r.kind
            JOIN licence l ON l.code = r.licence
            LEFT JOIN LATERAL (
                SELECT q.status, q.sent_at, q.credit_line, q.terms FROM permission_request q
                WHERE q.reference_id = r.id AND q.status <> 'withdrawn'
                ORDER BY q.created_at DESC LIMIT 1
            ) p ON true
            WHERE r.book_id = @bookId AND r.archived_at IS NULL
              AND (c.needs_permission OR p.status IS NOT NULL)
            ORDER BY c.number NULLS LAST, lower(r.title), r.id
            """,
            new { bookId }, cancellationToken: ct));
        return rows.Select(r => new PermissionsLogRow(
            r.Title, CslNames.Label(r.AuthorsJson), r.Year, r.Kind, r.Licence, r.RightsHolder, r.PermissionsContact,
            r.Uses, r.QuotedWords, r.Status, r.Reason, r.PermissionStatus,
            r.SentAt is { } sentAt ? DisplayDate.DayOf(sentAt, clock.LocalTimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null, r.CreditLine,
            // permission_request.terms holds the grant's terms, or the refusal note (the table has no note column).
            r.PermissionStatus == "granted" ? r.Terms : null, r.PermissionStatus == "refused" ? r.Terms : null)).ToList();
    }

    private SourceRowDto ToDto(SourceRow row)
    {
        var chapters = row.ChapterNumbersJson is null
            ? []
            : JsonSerializer.Deserialize<int[]>(row.ChapterNumbersJson)!.Order().ToArray();
        var usedIn = chapters.Length == 0 ? string.Empty : "Ch " + string.Join(", ", chapters);
        var cleared = row.SignedOffAt is null
            ? null
            : $"Cleared by {Initials(row.SignedOffBy ?? "Codex")} · {DisplayDate.ShortDay(row.SignedOffAt.Value, clock.GetUtcNow(), clock.LocalTimeZone)}";
        return new SourceRowDto(
            row.Id, row.Number, row.Title, CslNames.Label(row.AuthorsJson), row.Year, row.Publisher, row.Kind,
            row.KindLabel, row.LicenceLabel, row.Status, row.Reason, row.Uses, usedIn, row.QuotedWords, row.Limit,
            row.Uses > 0, cleared, ReminderLabel(row), row.RemindDue == true);
    }

    // "Reminder due" once the date has come, else "Reminder due in 14 days"; null when no sent request waits on a reply.
    private string? ReminderLabel(SourceRow row)
    {
        if (row.RemindAt is not { } at)
        {
            return null;
        }

        if (row.RemindDue == true)
        {
            return "Reminder due";
        }

        var days = DisplayDate.DaysUntil(at, clock.GetUtcNow(), clock.LocalTimeZone);
        return days <= 1 ? "Reminder due in 1 day" : $"Reminder due in {days} days";
    }

    // "Joel Dickson" -> "JD": the first letter of each word, as the book bar's avatar shows the user.
    private static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0])));

    private static JsonElement ParseCsl(string csl)
    {
        using var document = JsonDocument.Parse(csl);
        return document.RootElement.Clone();
    }

    private static SourceExportFile Text(string fileName, string contentType, string content) =>
        new(fileName, contentType, new UTF8Encoding(false).GetBytes(content));

    private static string Slug(string title)
    {
        var slug = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var result = slug.ToString().Trim('-');
        return result.Length == 0 ? "book" : result;
    }

    private static async Task RequireBookAsync(NpgsqlConnection connection, Guid bookId, CancellationToken ct)
    {
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: ct)))
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }
    }

    private sealed record SourceRow(
        Guid Id, int? Number, string Title, string AuthorsJson, int? Year, string? Publisher, string Kind,
        string KindLabel, string LicenceLabel, string Status, string Reason, int Uses, string? ChapterNumbersJson,
        int QuotedWords, int Limit, DateTimeOffset? SignedOffAt, string? SignedOffBy, DateTimeOffset? RemindAt, bool? RemindDue);

    private sealed record CslRow(Guid SourceId, string Status, string Csl, string? CreditLine);

    private sealed record PermissionRow(
        string Title, string AuthorsJson, int? Year, string Kind, string Licence, string? RightsHolder,
        string? PermissionsContact, int Uses, int QuotedWords, string Status, string Reason, string? PermissionStatus,
        DateTimeOffset? SentAt, string? CreditLine, string? Terms);
}
