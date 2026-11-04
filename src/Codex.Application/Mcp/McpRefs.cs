using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agoda.IoC.Core;
using Codex.Application.Features.Structure;
using Codex.Contracts;
using Dapper;
using ModelContextProtocol.Server;
using Npgsql;

namespace Codex.Application.Mcp;

// Turns what the author says into ids (#20 section 2.1), at call time, among the books the current user can open: their
// own and those shared with them (#80, book_access), together. Resolving a ref checks no role; a tool that writes
// checks its own minimum through IBookAccess once it has the book (docs/mcp.md → Roles):
//   BookRef   id · title · "<series>/<number>"   ("The Ashen Crown/3" → Hollow Throne)
//   ItemRef   id · "5" · "5.2" · "act:2" · "ch:<slug>" · "Ch 5 Sc 2"
//   EntryRef  id · "e:<slug>" · name · alias, in a book's scope (book + series) or across the library
// A ref that matches more than one thing fails with `ambiguous` and lists them; no match fails with `not_found` and
// the close matches, each naming its owner. A book title also matches a copy that only adds a bracketed suffix, so "Product Engineering"
// names both it and "Product Engineering (v1)" (#20's own example) and the agent must pick an id.
[RegisterSingleton]
public sealed partial class McpRefs(NpgsqlDataSource dataSource, ICurrentUser currentUser, OutlineQuery outlines, McpRefMemory memory)
{
    private const int MaxCloseMatches = 5;

    private sealed record EntryRow(Guid Id, string Name, string Slug, string Aliases, Guid OwnerId, string OwnerKind, Guid? BookId, bool InScope);

    // The session a call belongs to, for stale_ref: the one stdio session (McpStdioTransport is registered only by the
    // Codex.Mcp process), or an HTTP session id. Streamable HTTP is stateless in SDK 2.2.0 (and sessions leave the
    // protocol with 2026-07-28), so an HTTP call usually has no session: null, and the stale check is skipped rather
    // than shared between every HTTP client.
    public static string? SessionKey(McpServer server) =>
        server.SessionId ?? (server.Services?.GetService(typeof(McpStdioTransport)) as McpStdioTransport)?.SessionKey;

    // null or blank: the most recently edited live book (resume_work's default).
    public async Task<McpBook> BookAsync(string? reference, CancellationToken cancellationToken)
    {
        var books = await LoadBooksAsync("book", cancellationToken);
        if (string.IsNullOrWhiteSpace(reference))
        {
            return books.Where(b => !b.Archived).MaxBy(b => b.LastEditedAt)
                   ?? throw McpToolException.NotFound("There are no books in the Library yet.");
        }
        return Pick(books, reference.Trim(), "book");
    }

    // The book or series with this id, when the current user can open it.
    public async Task<McpBook?> AccessibleAsync(Guid id, CancellationToken cancellationToken) =>
        (await AccessibleAllAsync(cancellationToken)).GetValueOrDefault(id);

    public async Task<McpBook> SeriesAsync(string reference, CancellationToken cancellationToken)
        => Pick(await LoadBooksAsync("series", cancellationToken), reference.Trim(), "series");

    // Every book and series the current user can open, by id, in one query (search resolves many hits against it).
    public async Task<IReadOnlyDictionary<Guid, McpBook>> AccessibleAllAsync(CancellationToken cancellationToken) =>
        (await LoadBooksAsync(null, cancellationToken)).ToDictionary(b => b.Id);

    // The book's outline as of now, recorded as what this session has seen (see McpRefMemory). With no session
    // (stateless HTTP) nothing is recorded and nothing can be stale.
    public async Task<BookOutline> OutlineAsync(McpBook book, string? sessionKey, CancellationToken cancellationToken)
    {
        var dto = await outlines.GetAsync(book.Id, cancellationToken);
        var outline = new BookOutline(book, dto, null);
        if (sessionKey == null)
        {
            return outline;
        }
        var previous = memory.Exchange(sessionKey, book.Id, outline.Numbers());
        return new BookOutline(book, dto, previous);
    }

    // An act, chapter or scene of the outline's book.
    public static McpItem Item(BookOutline outline, string reference)
    {
        var text = reference.Trim();
        if (Guid.TryParse(text, out var id))
        {
            return outline.ItemById(id)
                   ?? throw McpToolException.NotFound($"{outline.Book.Title} has no live act, chapter or scene with id {id}.");
        }
        if (text.StartsWith("ch:", StringComparison.OrdinalIgnoreCase))
        {
            var slug = text[3..].Trim();
            if (slug.Length == 0)
            {
                throw McpToolException.Invalid("ref", "\"ch:\" needs a chapter slug after it, e.g. \"ch:shipping-small\".");
            }
            var chapter = outline.Chapters.Concat(outline.ArchivedChapters)
                .FirstOrDefault(c => string.Equals(c.Dto.Slug, slug, StringComparison.OrdinalIgnoreCase))
                ?? throw McpToolException.NotFound($"{outline.Book.Title} has no chapter ch:{slug}.",
                    outline.Chapters.Where(c => c.Dto.Slug.Contains(slug, StringComparison.OrdinalIgnoreCase))
                        .Take(MaxCloseMatches).Select(c => c.Item(outline.Book.Id)).ToList());
            return chapter.Dto.Archived
                ? throw McpToolException.NotFound($"Chapter \"{chapter.Dto.Title}\" is archived.")
                : chapter.Item(outline.Book.Id);
        }

        var number = DisplayNumber(text)
                     ?? throw McpToolException.Invalid("ref",
                         $"\"{text}\" is not an act, chapter or scene ref: use an id, \"5\", \"5.2\", \"act:2\" or \"ch:<slug>\".");
        var current = outline.Numbers().TryGetValue(number, out var currentId) ? outline.ItemById(currentId) : null;
        if (outline.Previous != null && outline.Previous.TryGetValue(number, out var seenId) && seenId != current?.Id)
        {
            var before = outline.ItemById(seenId) ?? new McpItem(seenId, KindOf(number), "(no longer in the outline)", CodexLinks.Plan(outline.Book.Id), null);
            var now = current ?? new McpItem(Guid.Empty, KindOf(number), "(nothing)", CodexLinks.Plan(outline.Book.Id), number);
            throw McpToolException.StaleRef(
                $"\"{number}\" has changed since this session last read {outline.Book.Title}: it was {before.Label}, it is now {now.Label}. Use ids from a fresh read.",
                before, now);
        }
        return current ?? throw McpToolException.NotFound($"{outline.Book.Title} has no {KindOf(number)} {number}.");
    }

    // An entry in the book's scope (its own and its series' entries), or anywhere in the Library when book is null.
    // An entry that exists only outside the book's scope fails with out_of_scope and the scope that was checked
    // (#20 2.3), not not_found.
    public async Task<McpEntry> EntryAsync(string reference, McpBook? book, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<EntryRow>(new CommandDefinition(
            """
            SELECT e.id, e.name, e.slug, to_json(e.aliases)::text AS Aliases, o.id AS OwnerId, o.kind AS OwnerKind,
                   (SELECT bk.id FROM book_scope bs JOIN book bk ON bk.id = bs.book_id
                    JOIN book_access ba ON ba.book_id = bk.id AND ba.user_id = @user
                    WHERE bs.scope_id = o.id AND bk.archived_at IS NULL
                      AND NOT EXISTS (SELECT 1 FROM book bsr WHERE bsr.id = bk.series_id AND bsr.archived_at IS NOT NULL)
                    ORDER BY bk.last_edited_at DESC, bk.id LIMIT 1) AS BookId,
                   (@bookId::uuid IS NULL OR e.book_id IN (SELECT scope_id FROM book_scope WHERE book_id = @bookId::uuid)) AS InScope
            FROM kb_entry e
            JOIN book o ON o.id = e.book_id
            JOIN book_access a ON a.book_id = o.id AND a.user_id = @user
            WHERE e.archived_at IS NULL
            ORDER BY e.name, e.id
            """,
            new { user = currentUser.Id, bookId = book?.Id }, cancellationToken: cancellationToken))).ToList();

        var text = reference.Trim();
        var where = book == null ? "the Library" : $"{book.Title}'s Knowledge base";
        List<EntryRow> all;
        var bySlug = false;
        if (Guid.TryParse(text, out var id))
        {
            all = rows.Where(r => r.Id == id).ToList();
        }
        else if (text.StartsWith("e:", StringComparison.OrdinalIgnoreCase))
        {
            bySlug = true;
            all = rows.Where(r => string.Equals(r.Slug, text[2..].Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            all = rows.Where(r => Same(r.Name, text) || AliasesOf(r).Any(a => Same(a, text))).ToList();
        }
        var matches = all.Where(r => r.InScope).ToList();
        if (bySlug && book != null && matches.Count > 1 && matches.Count(r => r.OwnerId == book.Id) == 1)
        {
            // A book's own entry wins over a same-slug series entry (kb_entry_slug_uq); a shared name stays ambiguous.
            matches = matches.Where(r => r.OwnerId == book.Id).ToList();
        }

        if (matches.Count == 0 && all.Count > 0 && book != null)
        {
            throw McpToolException.OutOfScope(
                $"\"{text}\" is a Knowledge base entry outside {book.Title}'s scope (the book and its series).",
                all.Take(MaxCloseMatches).Select(r => ToEntry(r, null).Item).ToList(), ScopeOf(book));
        }
        return matches.Count switch
        {
            1 => ToEntry(matches[0], book),
            0 => throw McpToolException.NotFound($"No Knowledge base entry \"{text}\" in {where}.",
                rows.Where(r => r.InScope && (r.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                                              || AliasesOf(r).Any(a => a.Contains(text, StringComparison.OrdinalIgnoreCase))))
                    .Take(MaxCloseMatches).Select(r => ToEntry(r, book).Item).ToList()),
            _ => throw McpToolException.Ambiguous($"\"{text}\" names {matches.Count} Knowledge base entries in {where}.",
                matches.Select(r => ToEntry(r, book).Item).ToList()),
        };
    }

    // The book and the series a book's Knowledge base scope is made of.
    public static IReadOnlyList<McpItem> ScopeOf(McpBook book) =>
        book.SeriesId is { } seriesId
            ? [book.Item, new McpItem(seriesId, "series", book.SeriesTitle ?? "", CodexLinks.Series(seriesId))]
            : [book.Item];

    // A series-owned entry opens in a live book of the series (the most recently edited one the caller can open), never
    // at the series id, which is not a book the Plan screen can open.
    private static McpEntry ToEntry(EntryRow row, McpBook? book) => new(row.Id, row.Name, row.OwnerId, book?.Id ?? row.BookId);

    private static McpBook Pick(List<McpBook> books, string text, string kind)
    {
        List<McpBook> matches;
        if (Guid.TryParse(text, out var id))
        {
            matches = books.Where(b => b.Id == id).ToList();
        }
        else if (kind == "book" && SeriesNumberRef().Match(text) is { Success: true } m)
        {
            // A number too big for any book matches none (not_found), rather than overflowing.
            matches = int.TryParse(m.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? books.Where(b => !b.Archived && b.SeriesNumber == number && Same(b.SeriesTitle, m.Groups["series"].Value)).ToList()
                : [];
        }
        else
        {
            matches = books.Where(b => !b.Archived && (Same(b.Title, text) || Same(BaseTitle(b.Title), text))).ToList();
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw McpToolException.NotFound($"No {kind} \"{text}\" in the Library.",
                books.Where(b => !b.Archived && (b.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                                                 || text.Contains(b.Title, StringComparison.OrdinalIgnoreCase)))
                    .Take(MaxCloseMatches).Select(b => b.Candidate).ToList()),
            _ => throw McpToolException.Ambiguous($"\"{text}\" matches {matches.Count} {kind} titles; use one of their ids.",
                matches.Select(b => b.Candidate).ToList()),
        };
    }

    // kind: book, series, or null for both. Owned and shared together, each with the caller's role and its owner.
    private async Task<List<McpBook>> LoadBooksAsync(string? kind, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<McpBook>(new CommandDefinition(
            """
            SELECT b.id, b.kind, b.title, b.series_id AS SeriesId, s.title AS SeriesTitle, b.series_number AS SeriesNumber,
                   b.status, (b.archived_at IS NOT NULL OR s.archived_at IS NOT NULL) AS Archived, b.last_edited_at AS LastEditedAt,
                   a.role AS Role, u.id AS OwnerId, u.display_name AS OwnerName
            FROM book b
            JOIN book_access a ON a.book_id = b.id AND a.user_id = @user
            JOIN app_user u ON u.id = b.owner_id
            LEFT JOIN book s ON s.id = b.series_id
            WHERE @kind::text IS NULL OR b.kind = @kind::text
            ORDER BY b.last_edited_at DESC, b.id
            """,
            new { user = currentUser.Id, kind }, cancellationToken: cancellationToken))).ToList();
    }

    // "5", "5.2", "act:2", "Ch 5", "Ch 5 · Sc 2", "chapter 5 scene 2" → the outline's own form; null for anything else.
    // A number too big for an int names nothing in any outline: not_found, not an overflow.
    private static string? DisplayNumber(string text)
    {
        if (ActRef().Match(text) is { Success: true } act)
        {
            return $"act:{Number(act.Groups[1].Value, "act", text)}";
        }
        if (NumberRef().Match(text) is not { Success: true } m)
        {
            return null;
        }
        var chapter = Number(m.Groups["chapter"].Value, "chapter", text);
        return m.Groups["scene"].Success
            ? $"{chapter}.{Number(m.Groups["scene"].Value, "scene", text)}"
            : chapter.ToString(CultureInfo.InvariantCulture);
    }

    private static int Number(string digits, string kind, string reference) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw McpToolException.NotFound($"There is no {kind} \"{reference}\": the number is out of range.");

    private static string KindOf(string number)
    {
        if (number.StartsWith("act:", StringComparison.Ordinal))
        {
            return "act";
        }
        return number.Contains('.', StringComparison.Ordinal) ? "scene" : "chapter";
    }

    private static List<string> AliasesOf(EntryRow row) =>
        JsonSerializer.Deserialize<List<string>>(row.Aliases, JsonSerializerOptions.Web) ?? [];

    private static bool Same(string? a, string? b) =>
        a != null && b != null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // "Product Engineering (v1)" → "Product Engineering".
    private static string BaseTitle(string title) => BracketSuffix().Replace(title, "");

    [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
    private static partial Regex BracketSuffix();

    [GeneratedRegex(@"^(?<series>.+)/\s*(?<number>\d+)$")]
    private static partial Regex SeriesNumberRef();

    [GeneratedRegex(@"^act\s*:?\s*(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ActRef();

    [GeneratedRegex(@"^(?:(?:ch|chapter)\s*)?(?<chapter>\d+)(?:(?:\s*[.·,]\s*|\s+)(?:(?:sc|scene)\s*)?(?<scene>\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex NumberRef();
}
