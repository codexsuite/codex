using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// A. Get my bearings: search ("find" in #20, renamed by M10). Books, chapters, scenes, Knowledge base entries (name or
// alias), sources and prose text containing the query, each with a label, a snippet and an open_url.
[McpServerToolType]
public sealed class SearchTools(McpRefs refs, McpQueries queries)
{
    private const int SnippetRadius = 120;
    private static readonly string[] Types = ["book", "chapter", "scene", "kb_entry", "source", "prose"];

    public sealed record Hit(string Type, McpItem Item, string? Book, string? Snippet);

    public sealed record SearchResult(IReadOnlyList<Hit> Hits) : McpResult;

    [McpServerTool(Name = "search", Title = "Where is …?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Finds books, chapters, scenes, Knowledge base entries (names and aliases), sources and prose containing the query " +
                 "(case-insensitive). Prose hits carry the sentence with the match.")]
    public async Task<SearchResult> SearchAsync(
        McpServer server,
        [Description("The words to look for.")] string query,
        [Description("Only this book (id, title or \"<series>/<number>\"); default the whole Library.")] string? book = null,
        [Description("Any of book, chapter, scene, kb_entry, source, prose; default all.")] string[]? types = null,
        [Description("At most this many hits (default 20, at most 100).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw McpToolException.Invalid("query", "query must not be empty.");
        }
        if (types?.FirstOrDefault(t => !Types.Contains(t)) is { } unknown)
        {
            throw McpToolException.Invalid("types", $"\"{unknown}\" is not one of {string.Join(", ", Types)}.");
        }
        if (limit is < 1 or > 100)
        {
            throw McpToolException.Invalid("limit", "limit is between 1 and 100.");
        }
        var scope = book == null ? null : await refs.BookAsync(book, cancellationToken);
        var rows = await queries.SearchAsync(query.Trim(), scope?.Id, types is { Length: > 0 } ? types : Types, limit, cancellationToken);

        var books = await refs.AccessibleAllAsync(cancellationToken);
        var outlines = new Dictionary<Guid, BookOutline>();
        var hits = new List<Hit>();
        foreach (var row in rows)
        {
            if (!books.TryGetValue(row.BookId, out var owner))
            {
                continue;
            }
            if (row.Kind is "chapter" or "scene" or "prose" && !outlines.ContainsKey(owner.Id))
            {
                outlines[owner.Id] = await refs.OutlineAsync(owner, McpRefs.SessionKey(server), cancellationToken);
            }
            var item = row.Kind switch
            {
                "book" => owner.Item,
                "chapter" or "scene" or "prose" => outlines[owner.Id].ItemById(row.Id),
                "kb_entry" => new McpEntry(row.Id, row.Label, owner.Id, row.LinkBookId).Item,
                _ => new McpItem(row.Id, "source", row.Label, CodexLinks.References(owner.Id, row.Id)),
            };
            if (item != null)
            {
                hits.Add(new Hit(row.Kind, item,
                    owner.Title, row.Text == null ? null : SnippetOf(row.Text, query.Trim())));
            }
        }
        return new SearchResult(hits);
    }

    // The sentence holding the match, as plain text; a window around it when the sentence is long.
    private static string SnippetOf(string markdown, string query)
    {
        var text = McpLabels.Plain(string.Join(' ', markdown.Split('\n').Where(l => !l.StartsWith("::", StringComparison.Ordinal))));
        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return text.Length <= 2 * SnippetRadius ? text : text[..(2 * SnippetRadius)] + "…";
        }
        var start = Math.Max(text.LastIndexOfAny(['.', '!', '?'], Math.Max(at - 1, 0)) + 1, at - SnippetRadius);
        var endMark = text.IndexOfAny(['.', '!', '?'], at + query.Length);
        var end = Math.Min(endMark < 0 ? text.Length : endMark + 1, at + query.Length + SnippetRadius);
        return text[start..end].Trim();
    }
}
