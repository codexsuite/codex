using System.Text;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbAliasQueries
{
    Task<KbAliasCheckDto> CheckAsync(Guid entryId, string? alias, CancellationToken cancellationToken);
}

// Looks at a proposed alias before or after it is saved: which other entry in scope already answers to it, and how
// many whole words of prose it would underline (case-insensitive, the entry's own book and its series' other books).
// Unknown entry 404; a blank alias 400 validation.
[RegisterSingleton]
public sealed class KbAliasQueries(NpgsqlDataSource dataSource, IBookAccess access) : IKbAliasQueries
{
    private const int ShortLetters = 3;
    private const int CommonMatches = 25;

    public async Task<KbAliasCheckDto> CheckAsync(Guid entryId, string? alias, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.View, cancellationToken);
        var text = alias?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw ApiException.Validation("Alias cannot be blank.");
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT book_id FROM kb_entry WHERE id = @entryId AND archived_at IS NULL", new { entryId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
        var collides = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT e.name FROM kb_entry e
            WHERE e.archived_at IS NULL AND e.id <> @entryId
              AND e.book_id IN (SELECT scope_id FROM book_scope WHERE book_id = @bookId)
              AND (lower(e.name) = lower(@text) OR EXISTS (SELECT 1 FROM unnest(e.aliases) a WHERE lower(a) = lower(@text)))
            ORDER BY e.name LIMIT 1
            """,
            new { entryId, bookId, text }, cancellationToken: cancellationToken));
        var matches = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::integer
            FROM prose p
            JOIN scene s ON s.id = p.scene_id AND s.archived_at IS NULL
            JOIN book b ON b.id = s.book_id AND b.deleted_at IS NULL
            CROSS JOIN LATERAL regexp_matches(p.body, @pattern, 'gi')
            WHERE s.book_id IN (SELECT book_id FROM book_scope WHERE scope_id = @bookId)
            """,
            new { bookId, pattern = WholeWord(text) }, cancellationToken: cancellationToken));
        return new KbAliasCheckDto(collides, matches, text.Count(char.IsLetter) <= ShortLetters || matches >= CommonMatches);
    }

    // The text as a regular expression that matches it literally, not inside a longer word.
    private static string WholeWord(string text)
    {
        var pattern = new StringBuilder(@"(?<!\w)");
        foreach (var c in text)
        {
            if (@"\^$.|?*+()[]{}".Contains(c, StringComparison.Ordinal))
            {
                pattern.Append('\\');
            }
            pattern.Append(c);
        }
        return pattern.Append(@"(?!\w)").ToString();
    }
}
