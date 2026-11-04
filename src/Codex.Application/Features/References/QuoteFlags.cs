using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

// The unflagged-quotes inbox. Both resolutions answer with the book's remaining open flags. Unknown flag: 404;
// a flag already resolved: 409; a blank note on Other source: 400.
public interface IQuoteFlags
{
    Task<IReadOnlyList<QuoteFlagDto>> ListAsync(Guid bookId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuoteFlagDto>> NotAQuoteAsync(Guid flagId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QuoteFlagDto>> OtherSourceAsync(Guid flagId, string? note, CancellationToken cancellationToken);
}

[RegisterSingleton]
public sealed class QuoteFlags(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access) : IQuoteFlags
{
    public async Task<IReadOnlyList<QuoteFlagDto>> ListAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: cancellationToken)))
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }

        return await OpenAsync(connection, bookId, cancellationToken);
    }

    public async Task<IReadOnlyList<QuoteFlagDto>> NotAQuoteAsync(Guid flagId, CancellationToken cancellationToken)
    {
        await RequireFlagAsync(flagId, cancellationToken);
        return await ResolveAsync(flagId, null, cancellationToken);
    }

    public async Task<IReadOnlyList<QuoteFlagDto>> OtherSourceAsync(Guid flagId, string? note, CancellationToken cancellationToken)
    {
        await RequireFlagAsync(flagId, cancellationToken);
        if (string.IsNullOrWhiteSpace(note))
        {
            throw ApiException.Validation("Say where the quote comes from.");
        }

        return await ResolveAsync(flagId, note.Trim(), cancellationToken);
    }

    private Task<Guid?> RequireFlagAsync(Guid flagId, CancellationToken cancellationToken)
        => access.RequireAsync(BookItem.QuoteFlag, flagId, BookRole.Write, cancellationToken, $"Quote flag {flagId} was not found.");

    // Dismissed keeps the row, so its fingerprint is never raised again for the book.
    private async Task<IReadOnlyList<QuoteFlagDto>> ResolveAsync(Guid flagId, string? note, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var updated = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            UPDATE quote_flag SET status = 'dismissed', note = @note, resolved_by = @userId, resolved_at = now()
            WHERE id = @flagId AND status = 'open'
            RETURNING book_id
            """,
            new { flagId, note, userId = currentUser.Id }, cancellationToken: cancellationToken));
        if (updated is { } bookId)
        {
            return await OpenAsync(connection, bookId, cancellationToken);
        }

        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM quote_flag WHERE id = @flagId)", new { flagId }, cancellationToken: cancellationToken)))
        {
            throw ApiException.NotFound($"Quote flag {flagId} was not found.");
        }

        throw ApiException.Conflict("This quote has already been resolved.");
    }

    private static async Task<IReadOnlyList<QuoteFlagDto>> OpenAsync(NpgsqlConnection connection, Guid bookId, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT f.id AS Id, f.scene_id AS SceneId, co.chapter_number::integer AS ChapterNumber, co.title AS ChapterTitle,
                   ss.scene_number::integer AS SceneNumber, f.reason AS Reason, f.excerpt AS Excerpt,
                   f.reference_id AS ReferenceId, r.title AS ReferenceTitle, r.authors::text AS Authors, r.year AS Year,
                   round(f.match_score * 100)::integer AS MatchPercent, f.locator AS Locator
            FROM quote_flag f
            JOIN scene_stats ss ON ss.scene_id = f.scene_id
            JOIN chapter_outline co ON co.id = ss.chapter_id
            LEFT JOIN reference r ON r.id = f.reference_id AND r.archived_at IS NULL
            WHERE f.book_id = @bookId AND f.status = 'open'
            ORDER BY co.reading_order, ss.scene_number, f.created_at, f.id
            """,
            new { bookId }, cancellationToken: cancellationToken));
        return rows.Select(Map).ToList();
    }

    private static QuoteFlagDto Map(Row row)
    {
        var label = row.ReferenceTitle is null ? null : Label(row);
        var why = "In quotation marks, with no source marker";
        if (row.Reason == "source_match" && label is not null)
        {
            var locator = row.Locator is null ? string.Empty : $", {row.Locator}";
            why = $"{row.MatchPercent}% match with {label}{locator}";
        }

        return new QuoteFlagDto(
            row.Id, row.SceneId, row.ChapterNumber, row.ChapterTitle, row.SceneNumber, row.Reason, why, row.Excerpt,
            row.ReferenceId, label, row.MatchPercent, row.Locator);
    }

    private static string Label(Row row)
    {
        var authors = CslNames.Label(row.Authors ?? "[]");
        var parts = new[] { authors, row.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty }.Where(p => p.Length > 0);
        var label = string.Join(", ", parts);
        return label.Length > 0 ? label : row.ReferenceTitle!;
    }

    private sealed record Row(
        Guid Id, Guid SceneId, int? ChapterNumber, string ChapterTitle, int SceneNumber, string Reason, string Excerpt,
        Guid? ReferenceId, string? ReferenceTitle, string? Authors, int? Year, int? MatchPercent, string? Locator);
}
