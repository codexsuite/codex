using System.Text.Json;
using Agoda.IoC.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Mcp;

// The queries behind list_kb_entries: a page of a book's entries in screen order, and the scene counts for that page.
[RegisterSingleton]
public sealed class KbListQueries(NpgsqlDataSource dataSource)
{
    public sealed record Row(Guid Id, string Name, string Category, Guid OwnerId, IReadOnlyList<string> Aliases, IReadOnlyList<string> Tags, bool AlwaysInContext);

    private sealed record RawRow(Guid Id, string Name, string Category, Guid OwnerId, string Aliases, string Tags, bool AlwaysInContext, long Total);

    // One page of the book's entries in screen order, with the number of entries in scope (of the category, when given).
    // The book was resolved through the ownership rule already; the scope view only reaches the book's own scope rows.
    public async Task<(IReadOnlyList<Row> Rows, int Total)> PageAsync(
        Guid bookId, string? category, int offset, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<RawRow>(new CommandDefinition(
            """
            SELECT e.id, e.name, bc.name AS Category, e.book_id AS OwnerId, to_json(e.aliases)::text AS Aliases, to_json(e.tags)::text AS Tags,
                   e.always_in_context AS AlwaysInContext, count(*) OVER () AS Total
            FROM book_scope bs
            JOIN kb_entry e ON e.book_id = bs.scope_id AND e.archived_at IS NULL
            JOIN kb_category c ON c.id = e.category_id
            JOIN book_kb_category bc ON bc.book_id = bs.book_id AND bc.name = c.name
            WHERE bs.book_id = @bookId AND (@category::text IS NULL OR lower(bc.name) = lower(@category::text))
            ORDER BY bc.position COLLATE "C", bc.name, e.position COLLATE "C", e.id
            OFFSET @offset LIMIT @limit
            """,
            new { bookId, category, offset, limit }, cancellationToken: cancellationToken))).ToList();
        if (rows.Count > 0)
        {
            return (rows.Select(r => new Row(r.Id, r.Name, r.Category, r.OwnerId, Strings(r.Aliases), Strings(r.Tags), r.AlwaysInContext)).ToList(), (int)rows[0].Total);
        }
        // An empty page: past the end (or an empty category), so the total needs its own count.
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::integer
            FROM book_scope bs
            JOIN kb_entry e ON e.book_id = bs.scope_id AND e.archived_at IS NULL
            JOIN kb_category c ON c.id = e.category_id
            WHERE bs.book_id = @bookId AND (@category::text IS NULL OR lower(c.name) = lower(@category::text))
            """,
            new { bookId, category }, cancellationToken: cancellationToken));
        return ([], total);
    }

    private static string[] Strings(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];

    public async Task<IReadOnlyList<string>> CategoryNamesAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT name FROM book_kb_category WHERE book_id = @bookId ORDER BY position COLLATE \"C\", name",
            new { bookId }, cancellationToken: cancellationToken))).ToList();
    }

    // Distinct live scenes using each entry, over every book that uses it: the count get_kb_entry reports as used_in_scenes.
    public async Task<IReadOnlyDictionary<Guid, int>> SceneCountsAsync(Guid[] entryIds, CancellationToken cancellationToken)
    {
        if (entryIds.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<(Guid Id, int Scenes)>(new CommandDefinition(
            """
            SELECT ku.kb_entry_id, count(DISTINCT ku.scene_id)::integer
            FROM kb_entry_use ku
            JOIN book b ON b.id = ku.book_id AND b.deleted_at IS NULL
            WHERE ku.kb_entry_id = ANY (@entryIds)
            GROUP BY ku.kb_entry_id
            """,
            new { entryIds }, cancellationToken: cancellationToken))).ToDictionary(r => r.Id, r => r.Scenes);
    }
}
