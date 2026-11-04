using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Shell;

public interface IShellQueries
{
    Task<LibraryDto> GetLibraryAsync(LibraryQuery query, CancellationToken cancellationToken);

    Task<BookHeaderDto> GetBookHeaderAsync(Guid bookId, CancellationToken cancellationToken);
}

// A book is archived when it is, or when its series is (archiving a series leaves its books' rows alone).
// Word counts always come from scene_stats (which keeps archived books), so an archived book still has its words.
// The access rule (#80): the Library lists every live book the caller can open (book_access: their own, and books
// shared with them directly or through a series), each with the caller's role and its owner. A series appears when
// the caller can open its row, and shows only the books they can open, so a book shared alone sits under its series'
// heading without the others; the roll-up counts those live books only, as book_stats would for the owner. The
// archived shelf is the owner's: archiving drops a book's own grants, and only the owner can restore it.
[RegisterSingleton]
public sealed class ShellQueries(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access, TimeProvider timeProvider)
    : IShellQueries
{
    private const string BookNotFound = "That book doesn't exist.";

    private const string BookColumns = """
        b.id, b.title, b.subtitle, b.author_name AS AuthorName, b.series_id AS SeriesId, s.title AS SeriesName,
        b.series_number AS SeriesNumber, b.status, b.cover_palette AS CoverPalette,
        coalesce(w.words, 0)::integer AS WordCount, b.target_word_count AS TargetWords,
        CASE WHEN b.target_word_count IS NOT NULL
             THEN round(100.0 * coalesce(w.words, 0) / b.target_word_count, 1) END AS ProgressPct,
        b.last_edited_at AS LastEditedAt, (b.archived_at IS NOT NULL OR s.archived_at IS NOT NULL) AS Archived, ca.id AS CoverAssetId,
        EXISTS (SELECT 1 FROM asset_rendition r WHERE r.asset_id = ca.id AND r.purpose = 'web') AS CoverHasWeb,
        coalesce(b.status = 'published' AND b.last_edited_at > (SELECT max(j.created_at) FROM export_job j
             WHERE j.book_id = b.id AND j.kind = 'source' AND j.settings ->> 'snapshot' = 'true'), false) AS EditedSincePublication,
        a.role AS Role, b.owner_id AS OwnerId, o.display_name AS OwnerName
        FROM book b
        JOIN book_access a ON a.book_id = b.id AND a.user_id = @user
        JOIN app_user o ON o.id = b.owner_id
        LEFT JOIN book s ON s.id = b.series_id
        LEFT JOIN asset ca ON ca.id = b.cover_asset_id AND ca.archived_at IS NULL
        LEFT JOIN LATERAL (SELECT sum(ss.word_count) AS words FROM scene_stats ss WHERE ss.book_id = b.id) w ON true
        """;

    private sealed record BookRow(
        Guid Id, string Title, string? Subtitle, string? AuthorName, Guid? SeriesId, string? SeriesName,
        int? SeriesNumber, string Status, string? CoverPalette, int WordCount, int? TargetWords,
        decimal? ProgressPct, DateTimeOffset LastEditedAt, bool Archived, Guid? CoverAssetId, bool CoverHasWeb,
        bool EditedSincePublication, string Role, Guid OwnerId, string OwnerName);

    private sealed record BadgeRow(int Count, string? Tone);

    private sealed record SeriesRow(
        Guid Id, string Name, string? Description, int BookCount, int PublishedCount, int WordCount,
        DateTimeOffset LastEditedAt, bool PartlyShared, bool Archived);

    public async Task<LibraryDto> GetLibraryAsync(LibraryQuery query, CancellationToken cancellationToken)
    {
        var archived = query.Status == LibraryStatuses.Archived;
        var pattern = string.IsNullOrWhiteSpace(query.Q) ? null : $"%{EscapeLike(query.Q.Trim())}%";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var books = (await connection.QueryAsync<BookRow>(new CommandDefinition(
            $"""
            SELECT {BookColumns}
            WHERE b.kind = 'book'
              AND (b.archived_at IS NOT NULL OR s.archived_at IS NOT NULL) = @archived AND (NOT @archived OR a.role = 'owner')
              AND (@status::text IS NULL OR @archived OR b.status = @status)
              AND (@pattern::text IS NULL OR b.title ILIKE @pattern OR b.subtitle ILIKE @pattern
                   OR s.title ILIKE @pattern)
            """,
            new { user = currentUser.Id, archived, status = archived ? null : query.Status, pattern },
            cancellationToken: cancellationToken))).ToList();

        // An unfiltered shelf also lists series that have no live books yet (a new empty series, or one whose books are archived).
        var unfiltered = pattern is null && query.Status is null;
        var seriesIds = books.Where(b => b.SeriesId is not null).Select(b => b.SeriesId!.Value).Distinct().ToArray();
        var seriesRows = (await connection.QueryAsync<SeriesRow>(new CommandDefinition(
            """
            SELECT s.id, s.title AS Name, s.description, count(v.id)::int AS BookCount,
                   (count(v.id) FILTER (WHERE v.status = 'published'))::int AS PublishedCount,
                   coalesce(sum(v.words), 0)::int AS WordCount,
                   greatest(s.last_edited_at, max(v.last_edited_at)) AS LastEditedAt,
                   (s.owner_id <> @user AND NOT EXISTS (
                       SELECT 1 FROM book_grant g WHERE g.book_id = s.id AND g.user_id = @user AND g.archived_at IS NULL)) AS PartlyShared,
                   (s.archived_at IS NOT NULL) AS Archived
            FROM book s
            JOIN book_access sa ON sa.book_id = s.id AND sa.user_id = @user
            LEFT JOIN LATERAL (
                SELECT b.id, b.status, b.last_edited_at,
                       (SELECT sum(ss.word_count) FROM scene_stats ss WHERE ss.book_id = b.id) AS words
                FROM book b
                JOIN book_access ba ON ba.book_id = b.id AND ba.user_id = @user
                WHERE b.series_id = s.id AND b.kind = 'book' AND b.archived_at IS NULL
            ) v ON true
            WHERE s.kind = 'series'
              AND (CASE WHEN @archived
                        THEN s.id = ANY(@ids) OR (s.archived_at IS NOT NULL AND s.owner_id = @user AND @pattern::text IS NULL)
                        ELSE s.archived_at IS NULL AND (s.id = ANY(@ids) OR @unfiltered) END)
            GROUP BY s.id, s.title, s.description, s.last_edited_at, s.owner_id, s.archived_at
            """,
            new { user = currentUser.Id, ids = seriesIds, unfiltered, archived, pattern },
            cancellationToken: cancellationToken))).ToDictionary(s => s.Id);

        var inSeries = books.Where(b => b.SeriesId is { } id && seriesRows.ContainsKey(id)).ToList();
        var standalone = books.Except(inSeries);

        var series = seriesRows.Values
            .Select(s => (Row: s, Books: inSeries.Where(b => b.SeriesId == s.Id).OrderBy(b => b.SeriesNumber).ToList()))
            .ToList();

        return new LibraryDto(
            OrderSeries(series, query.Sort).Select(s => ToSeriesDto(s.Row, s.Books)).ToList(),
            OrderBooks(standalone, query).Select(ToBookDto).ToList());
    }

    public async Task<BookHeaderDto> GetBookHeaderAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, BookNotFound);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<BookRow>(new CommandDefinition(
            $"SELECT {BookColumns} WHERE b.id = @bookId AND b.kind = 'book'",
            new { bookId, user = currentUser.Id },
            cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound(BookNotFound);

        var scope = (await connection.QueryAsync<BookScopeEntryDto>(new CommandDefinition(
            """
            SELECT sc.id, sc.kind, sc.title
            FROM book_scope bs
            JOIN book sc ON sc.id = bs.scope_id
            WHERE bs.book_id = @bookId
            ORDER BY (sc.kind = 'book') DESC, sc.title
            """,
            new { bookId },
            cancellationToken: cancellationToken))).ToList();

        // Same rule as the References summary's Badge, read from the same view.
        var badge = await connection.QuerySingleOrDefaultAsync<BadgeRow>(new CommandDefinition(
            """
            SELECT CASE WHEN risk_count > 0 THEN risk_count ELSE needs_check_count END AS Count,
                   CASE WHEN risk_count > 0 THEN 'danger' WHEN needs_check_count > 0 THEN 'warn' END AS Tone
            FROM book_reference_summary
            WHERE book_id = @bookId
            """,
            new { bookId },
            cancellationToken: cancellationToken));

        return new BookHeaderDto(
            row.Id, row.Title, row.Subtitle, row.AuthorName, row.SeriesId, row.SeriesName, row.SeriesNumber,
            row.Status, BookCover.Palette(row.Id, row.CoverPalette), BookCover.Initials(row.Title),
            row.WordCount, row.TargetWords, scope, badge?.Count, badge?.Tone, row.LastEditedAt, Label(row.LastEditedAt),
            BookCover.Url(row.CoverAssetId, row.CoverHasWeb), row.Role, new BookOwnerDto(row.OwnerId, row.OwnerName));
    }

    private string Label(DateTimeOffset value) => DisplayDate.Relative(value, timeProvider);

    // Series order by the sort; progress uses the best progress among the series' books.
    private static IEnumerable<(SeriesRow Row, List<BookRow> Books)> OrderSeries(
        List<(SeriesRow Row, List<BookRow> Books)> series, string sort) => sort switch
        {
            LibrarySorts.Title or LibrarySorts.Series => series
                .OrderBy(s => s.Row.Name, StringComparer.OrdinalIgnoreCase),
            LibrarySorts.Progress => series
                .OrderByDescending(s => s.Books.Max(b => b.ProgressPct) ?? -1m)
                .ThenBy(s => s.Row.Name, StringComparer.OrdinalIgnoreCase),
            _ => series.OrderByDescending(s => s.Row.LastEditedAt),
        };

    // Standalone books follow the sort; group=status first orders them by lifecycle stage.
    private static IEnumerable<BookRow> OrderBooks(IEnumerable<BookRow> books, LibraryQuery query)
    {
        var stages = LibraryStatuses.All.ToList();
        var ordered = query.Group == LibraryGroups.Status
            ? books.OrderBy(b => stages.IndexOf(b.Status))
            : books.OrderBy(_ => 0);
        return query.Sort switch
        {
            LibrarySorts.Title or LibrarySorts.Series => ordered.ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase),
            LibrarySorts.Progress => ordered.ThenByDescending(b => b.ProgressPct ?? -1m)
                .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase),
            _ => ordered.ThenByDescending(b => b.LastEditedAt),
        };
    }

    private LibrarySeriesDto ToSeriesDto(SeriesRow row, List<BookRow> books) => new(
        row.Id, row.Name, row.Description, row.Archived ? books.Count : row.BookCount, row.PublishedCount, row.WordCount,
        row.LastEditedAt, Label(row.LastEditedAt), books.Select(ToBookDto).ToList(), row.PartlyShared, row.Archived);

    private LibraryBookDto ToBookDto(BookRow row) => new(
        row.Id, row.Title, row.Subtitle, row.AuthorName, row.SeriesId, row.SeriesName, row.SeriesNumber, row.Status,
        BookCover.Palette(row.Id, row.CoverPalette), BookCover.Initials(row.Title), row.WordCount,
        row.TargetWords, row.ProgressPct, row.LastEditedAt, Label(row.LastEditedAt), null, row.Archived,
        BookCover.Url(row.CoverAssetId, row.CoverHasWeb), row.EditedSincePublication, row.Role,
        new BookOwnerDto(row.OwnerId, row.OwnerName));

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
