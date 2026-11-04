using System.Globalization;
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
public sealed class SourceDetailQueries(NpgsqlDataSource dataSource, TimeProvider clock, IBookAccess access) : ISourceDetailQueries
{
    public async Task<SourceDetailDto> GetAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Source, sourceId, BookRole.View, cancellationToken, $"Source {sourceId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, sourceId, cancellationToken);
    }

    public async Task<SourceDetailDto> GetAsync(NpgsqlConnection connection, Guid sourceId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<SourceRow>(new CommandDefinition(
            """
            SELECT r.id AS Id, r.book_id AS BookId, c.number::integer AS Number, r.kind AS Kind, k.label AS KindLabel,
                   r.title AS Title, r.authors::text AS AuthorsJson, r.year AS Year, r.publisher AS Publisher,
                   r.edition AS Edition, r.isbn AS Isbn, r.doi AS Doi, r.url AS Url,
                   to_char(r.accessed_on, 'YYYY-MM-DD') AS AccessedOn, r.licence AS Licence, l.label AS LicenceLabel,
                   l.allows_reuse AS AllowsReuse, (l.needs_fair_use_decision AND r.fair_use_decided_at IS NULL) AS FairUseOpen,
                   r.rights_holder AS RightsHolder, r.fair_use_note AS FairUseNote, r.signed_off_at AS SignedOffAt,
                   u.display_name AS SignedOffBy, r.kb_entry_id AS EntryId, e.name AS EntryName, e.category_id AS EntryCategoryId,
                   c.status AS Status, c.reason AS Reason, c.quoted_words AS QuotedWords, c.lyric_words AS LyricWords,
                   c.figure_count AS FigureCount, c.data_count AS DataCount, ps.quote_word_limit AS Limit,
                   to_json(c.missing_fields)::text AS MissingJson
            FROM reference r
            JOIN reference_clearance c ON c.reference_id = r.id
            JOIN reference_kind k ON k.code = r.kind
            JOIN licence l ON l.code = r.licence
            JOIN book_publication_settings ps ON ps.book_id = r.book_id
            LEFT JOIN app_user u ON u.id = r.signed_off_by
            LEFT JOIN kb_entry e ON e.id = r.kb_entry_id
            WHERE r.id = @sourceId AND r.archived_at IS NULL
            """,
            new { sourceId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Source {sourceId} was not found.");

        var identifiers = (await connection.QueryAsync<IdentifierCheckDto>(new CommandDefinition(
            """
            SELECT c.field AS Field, c.status AS Status, c.resolved_url AS ResolvedUrl
            FROM reference_check c JOIN reference r ON r.id = c.reference_id
            WHERE c.reference_id = @sourceId AND c.checked_value = to_jsonb(r) ->> c.field
            ORDER BY c.field
            """,
            new { sourceId }, cancellationToken: cancellationToken))).ToList();

        var uses = (await connection.QueryAsync<UseRow>(new CommandDefinition(
            """
            SELECT x.id AS Id, x.scene_id AS SceneId, o.chapter_number::integer AS ChapterNumber, co.title AS ChapterTitle,
                   o.scene_number::integer AS SceneNumber, x.kind AS Kind, x.excerpt AS Excerpt, x.word_count AS Words,
                   x.locator AS Locator, (r.signed_off_at IS NOT NULL AND x.changed_at > r.signed_off_at) AS NewSinceSignOff,
                   (x.generation_id IS NOT NULL) AS Pending
            FROM reference_use_outline o
            JOIN reference_use x ON x.id = o.use_id
            JOIN reference r ON r.id = o.reference_id
            JOIN chapter_outline co ON co.id = o.chapter_id
            WHERE o.reference_id = @sourceId
            ORDER BY o.book_order
            """,
            new { sourceId }, cancellationToken: cancellationToken)))
            .Select(u => new SourceUseDto(u.Id, u.SceneId, u.ChapterNumber, u.ChapterTitle, u.SceneNumber, u.Kind, u.Excerpt, u.Words, u.Locator, u.NewSinceSignOff, u.Pending))
            .ToList();

        var kinds = (await connection.QueryAsync<SourceOptionDto>(new CommandDefinition(
            "SELECT code AS Code, label AS Label FROM reference_kind ORDER BY label", cancellationToken: cancellationToken))).ToList();
        var licences = (await connection.QueryAsync<SourceOptionDto>(new CommandDefinition(
            "SELECT code AS Code, label AS Label FROM licence ORDER BY label", cancellationToken: cancellationToken))).ToList();

        var history = (await connection.QueryAsync<EventRow>(new CommandDefinition(
            """
            SELECT ev.action AS Action, ev.detail::text AS DetailJson, ev.created_at AS At, u.display_name AS Who
            FROM reference_event ev LEFT JOIN app_user u ON u.id = ev.actor_id
            WHERE ev.reference_id = @sourceId
            ORDER BY ev.created_at DESC, ev.id DESC
            LIMIT 50
            """,
            new { sourceId }, cancellationToken: cancellationToken)))
            .Select(e => new SourceHistoryDto(DisplayDate.Relative(e.At, clock), e.Who ?? "Codex", SourceEvents.Text(e.Action, e.DetailJson)))
            .ToList();

        var permission = await PermissionAsync(connection, sourceId, cancellationToken);
        var missing = JsonSerializer.Deserialize<string[]>(row.MissingJson)!;
        var facts = new SourceFacts(
            row.Status, row.Reason, row.Licence, row.LicenceLabel, row.AllowsReuse, row.FairUseOpen, row.FairUseNote,
            row.QuotedWords, row.LyricWords, row.FigureCount, row.DataCount, row.Limit, missing, identifiers, row.SignedOffAt is not null);
        var verdict = new SourceVerdictDto(
            row.Status, row.Reason, SourceChecklist.NextAction(facts), row.SignedOffAt is null ? null : row.SignedOffBy ?? "Codex",
            row.SignedOffAt is null ? null : DisplayDate.Relative(row.SignedOffAt.Value, clock));
        var details = new SourceDetailsDto(
            row.Kind, row.Title, SourceAuthors.ToText(row.AuthorsJson), row.Year, row.Publisher, row.Edition, row.Isbn, row.Doi,
            row.Url, row.AccessedOn, row.Licence, row.RightsHolder, row.FairUseNote, missing, identifiers);
        return new SourceDetailDto(
            row.Id, row.BookId, row.Number, row.Kind, row.KindLabel, row.Title, CslNames.Label(row.AuthorsJson), row.Year, row.Publisher,
            verdict, SourceChecklist.Checks(facts), uses, row.QuotedWords, row.Limit,
            row.EntryId is null ? null : new SourceEntryDto(row.EntryId.Value, row.EntryName!, row.EntryCategoryId!.Value),
            details, kinds, licences, history, permission);
    }

    // The latest request that was not withdrawn: the same one reference_clearance reads.
    private async Task<PermissionDto?> PermissionAsync(NpgsqlConnection connection, Guid sourceId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<PermissionRow>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.status AS Status, p.to_address AS ToAddress, p.letter AS Letter, p.sent_at AS SentAt,
                   p.remind_at AS RemindAt, (p.remind_at IS NOT NULL AND p.remind_at <= now()) AS RemindDue,
                   p.responded_at AS RespondedAt, p.credit_line AS CreditLine, p.terms AS Terms,
                   p.last_reminded_at AS LastRemindedAt, p.fee_amount AS FeeAmount, r.title AS SourceTitle, a.filename AS FileName
            FROM permission_request p
            JOIN reference r ON r.id = p.reference_id
            LEFT JOIN LATERAL (
                SELECT s.filename FROM reference_attachment t JOIN asset s ON s.id = t.asset_id
                WHERE t.permission_request_id = p.id AND t.kind = 'permission_letter' AND t.archived_at IS NULL AND s.archived_at IS NULL
                ORDER BY t.created_at DESC LIMIT 1
            ) a ON true
            WHERE p.reference_id = @sourceId AND p.status <> 'withdrawn'
            ORDER BY p.created_at DESC LIMIT 1
            """,
            new { sourceId }, cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var zone = clock.LocalTimeZone;
        var waiting = row.Status is "draft" or "sent";
        var sent = row.SentAt is null ? null : $"Asked {DisplayDate.ShortDay(row.SentAt.Value, now, zone)}";
        string? remind = null;
        string? remindOn = null;
        if (row.RemindAt is { } due && waiting)
        {
            remind = row.RemindDue ? "Reminder due" : $"Reminder {DisplayDate.ShortDay(due, now, zone)}";
            remindOn = DisplayDate.DayOf(due, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var answer = row.Status == "granted" ? "Granted" : "Refused";
        var responded = row.RespondedAt is null ? null : $"{answer} {DisplayDate.ShortDay(row.RespondedAt.Value, now, zone)}";
        var followUp = row.Status == "sent" && row.SentAt is { } askedOn
            ? $"Hello,\n\nI am following up on my request of {DisplayDate.ShortDay(askedOn, now, zone)} for permission to use \"{row.SourceTitle}\". "
              + "I would be grateful for a reply when you have a moment, and I am happy to answer any questions.\n\nThank you."
            : null;
        var followedUp = row.LastRemindedAt is { } at ? $"Followed up {DisplayDate.ShortDay(at, now, zone)}" : null;
        return new PermissionDto(
            row.Id, row.Status, row.ToAddress, row.Letter, sent, remindOn, remind, row.RemindDue && waiting, responded,
            row.CreditLine, row.Status == "granted" ? row.Terms : null, row.Status == "refused" ? row.Terms : null,
            row.FileName is null ? null : new PermissionLetterDto(row.FileName, $"/api/permission-requests/{row.Id}/letter"),
            followUp, followedUp, row.FeeAmount);
    }


    private sealed record SourceRow(
        Guid Id, Guid BookId, int? Number, string Kind, string KindLabel, string Title, string AuthorsJson, int? Year,
        string? Publisher, string? Edition, string? Isbn, string? Doi, string? Url, string? AccessedOn, string Licence,
        string LicenceLabel, bool AllowsReuse, bool FairUseOpen, string? RightsHolder, string? FairUseNote,
        DateTimeOffset? SignedOffAt, string? SignedOffBy, Guid? EntryId, string? EntryName, Guid? EntryCategoryId,
        string Status, string Reason, int QuotedWords, int LyricWords, int FigureCount, int DataCount, int Limit,
        string MissingJson);

    private sealed record UseRow(
        Guid Id, Guid SceneId, int? ChapterNumber, string ChapterTitle, int SceneNumber, string Kind, string Excerpt,
        int Words, string? Locator, bool NewSinceSignOff, bool Pending);

    private sealed record PermissionRow(
        Guid Id, string Status, string ToAddress, string Letter, DateTimeOffset? SentAt, DateTimeOffset? RemindAt, bool RemindDue,
        DateTimeOffset? RespondedAt, string? CreditLine, string? Terms, DateTimeOffset? LastRemindedAt, decimal? FeeAmount,
        string SourceTitle, string? FileName);

    private sealed record EventRow(string Action, string DetailJson, DateTimeOffset At, string? Who);
}
