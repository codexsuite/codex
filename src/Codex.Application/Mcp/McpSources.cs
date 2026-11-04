using Agoda.IoC.Core;
using Codex.Application.Features.References;
using Codex.Contracts;
using Codex.Contracts.References;
using Dapper;
using Npgsql;

namespace Codex.Application.Mcp;

// Turns a SourceRef into a source (#20 section 2.1): an id, a marker number ("#4" or "4"), a cite key or a title, in
// one book (`book`, the most recently edited live book when omitted; an id names its own book). No match is
// not_found with the close ones, more than one is ambiguous. The History lines of an agent's call are written as
// the agent's by the References commands themselves (an EventProvenance argument; #20 2.5).
[RegisterSingleton]
public sealed class McpSources(McpRefs refs, ISourceQueries sources, NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access)
{
    private const int MaxCloseMatches = 5;

    public sealed record Source(McpBook Book, SourceRowDto Row)
    {
        public McpItem Item => ItemOf(Book, Row);
    }

    public static McpItem ItemOf(McpBook book, SourceRowDto row) =>
        new(row.Id, "source", Label(row.Number, row.Title), CodexLinks.References(book.Id, row.Id), row.Number is { } n ? $"#{n}" : null);

    public static string Label(int? number, string title) => number is { } n ? $"#{n} · {title}" : title;

    public async Task<Source> SourceAsync(string reference, string? book, CancellationToken cancellationToken)
    {
        var text = reference.Trim();
        if (text.Length == 0)
        {
            throw McpToolException.Invalid("source", "source is empty: give an id, a marker number like \"#4\", a cite key or a title.");
        }
        var bookRef = book;
        if (Guid.TryParse(text, out var id))
        {
            bookRef = (await BookOfAsync(id, cancellationToken) ?? throw McpToolException.NotFound($"No source {id}.")).ToString();
        }
        var owner = await refs.BookAsync(bookRef, cancellationToken);
        var rows = await sources.GetSourcesAsync(owner.Id, cancellationToken);
        List<SourceRowDto> matches;
        if (Guid.TryParse(text, out id))
        {
            matches = rows.Where(r => r.Id == id).ToList();
        }
        else if (Marker(text) is { } number)
        {
            matches = rows.Where(r => r.Number == number).ToList();
        }
        else
        {
            var key = await ByCiteKeyAsync(owner.Id, text, cancellationToken);
            matches = key == null
                ? rows.Where(r => string.Equals(r.Title.Trim(), text, StringComparison.OrdinalIgnoreCase)).ToList()
                : rows.Where(r => r.Id == key).ToList();
        }
        return matches.Count switch
        {
            1 => new Source(owner, matches[0]),
            0 => throw McpToolException.NotFound($"No source \"{text}\" in {owner.Title}.",
                rows.Where(r => r.Title.Contains(text, StringComparison.OrdinalIgnoreCase) || text.Contains(r.Title, StringComparison.OrdinalIgnoreCase))
                    .Take(MaxCloseMatches).Select(r => ItemOf(owner, r)).ToList()),
            _ => throw McpToolException.Ambiguous($"\"{text}\" names {matches.Count} sources in {owner.Title}; use an id.",
                matches.Select(r => ItemOf(owner, r)).ToList()),
        };
    }

    // A source a References tool is about to change: SourceAsync, then write on its book (#80), so a view or comment
    // role fails forbidden before anything is read or written.
    public async Task<Source> WritableAsync(string reference, string? book, CancellationToken cancellationToken)
    {
        var found = await SourceAsync(reference, book, cancellationToken);
        await access.RequireAsync(found.Book.Id, BookRole.Write, cancellationToken);
        return found;
    }

    // "#4" or "4" → 4. A bare number could be a title, so only a '#'-less all-digit text counts as the marker.
    private static int? Marker(string text)
    {
        var digits = text.StartsWith('#') ? text[1..] : text;
        return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    // The book of a live source, or of a quote flag, when the current user can open it (any role; the tool checks its own).
    public async Task<Guid?> BookOfAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            SELECT r.book_id FROM reference r JOIN book_access a ON a.book_id = r.book_id AND a.user_id = @user
            WHERE r.id = @sourceId AND r.archived_at IS NULL
            """,
            new { sourceId, user = currentUser.Id }, cancellationToken: cancellationToken));
    }

    public async Task<Guid?> BookOfFlagAsync(Guid flagId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT f.book_id FROM quote_flag f JOIN book_access a ON a.book_id = f.book_id AND a.user_id = @user WHERE f.id = @flagId",
            new { flagId, user = currentUser.Id }, cancellationToken: cancellationToken));
    }

    private async Task<Guid?> ByCiteKeyAsync(Guid bookId, string key, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM reference WHERE book_id = @bookId AND archived_at IS NULL AND lower(cite_key) = lower(@key)",
            new { bookId, key }, cancellationToken: cancellationToken));
    }
}
