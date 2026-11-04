using System.ComponentModel;
using System.Globalization;
using System.Text;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// E. Knowledge base, listing (#77): every entry a book can see, in the order the Knowledge base screen shows them
// (category position, then entry position), paged. Two queries for a page: the rows, then the scene counts for those rows.
[McpServerToolType]
public sealed class KbListTools(McpRefs refs, KbListQueries queries)
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const string CursorPrefix = "kb1:";

    public sealed record ListedEntry(
        McpItem Item, string Name, string Category, string Scope, IReadOnlyList<string> Aliases, IReadOnlyList<string> Tags,
        bool AlwaysInContext, int UsedInScenes);

    public sealed record ListKbEntriesResult(IReadOnlyList<ListedEntry> Entries, int Total, string? NextCursor) : McpResult;

    [McpServerTool(Name = "list_kb_entries", Title = "List the Knowledge base", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("The Knowledge base entries a book can see (its own and its series'; archived ones left out), in the order the Knowledge base " +
                 "screen shows them: category, then entry. Each has its id and open_url, name, category, scope (book or series), aliases, tags, " +
                 "always_in_context and used_in_scenes (live scenes that use it, as get_kb_entry counts them). Pages of limit (default 100, at " +
                 "most 500); next_cursor continues, absent (null) on the last page; total counts every entry in scope (of the category, when one is given).")]
    public async Task<ListKbEntriesResult> ListKbEntriesAsync(
        [Description("The book: id, title or \"<series>/<number>\".")] string book,
        [Description("Only this category (its name, any case).")] string? category = null,
        [Description("The cursor a previous call returned, to continue from there.")] string? cursor = null,
        [Description("Entries per page (default 100, at most 500).")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxLimit)
        {
            throw McpToolException.Invalid("limit", $"limit is between 1 and {MaxLimit}.");
        }
        var offset = ParseCursor(cursor);
        var target = await refs.BookAsync(book, cancellationToken);
        var name = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var (rows, total) = await queries.PageAsync(target.Id, name, offset, limit, cancellationToken);
        if (rows.Count == 0 && (offset > 0 || name != null))
        {
            if (offset > 0 && total > 0)
            {
                throw McpToolException.Invalid("cursor", "That cursor is past the end of this list; list again without one.");
            }
            if (name != null)
            {
                var categories = await queries.CategoryNamesAsync(target.Id, cancellationToken);
                if (!categories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    throw McpToolException.NotFound(
                        $"{target.Title}'s Knowledge base has no category \"{name}\" (it has {string.Join(", ", categories)}).");
                }
            }
        }

        var uses = await queries.SceneCountsAsync(rows.Select(r => r.Id).ToArray(), cancellationToken);
        var entries = rows.Select(r => new ListedEntry(
            new McpEntry(r.Id, r.Name, target.Id, target.Id).Item, r.Name, r.Category, r.OwnerId == target.Id ? "book" : "series",
            r.Aliases, r.Tags, r.AlwaysInContext, uses.GetValueOrDefault(r.Id))).ToList();
        var next = offset + rows.Count < total ? EncodeCursor(offset + rows.Count) : null;
        return new ListKbEntriesResult(entries, total, next) { Item = target.Item };
    }

    // Opaque to the caller: the offset into the ordered list, behind a version prefix.
    private static string EncodeCursor(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(CursorPrefix + offset.ToString(CultureInfo.InvariantCulture)));

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (text.StartsWith(CursorPrefix, StringComparison.Ordinal)
                && int.TryParse(text.AsSpan(CursorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
            {
                return offset;
            }
        }
        catch (FormatException)
        {
            // falls through to the error below
        }
        throw McpToolException.Invalid("cursor", "That is not a cursor this tool returned; list again without one.");
    }
}
