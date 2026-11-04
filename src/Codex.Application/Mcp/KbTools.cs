using System.ComponentModel;
using Codex.Application.Features.KnowledgeBase;
using Codex.Contracts.KnowledgeBase;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// E. Knowledge base, read side: get_kb_entry (M3) and find_kb_usages (#20; read-only, so phase 1). Adapters over the
// entry window's query (IKbEntryQueries), which already gathers the entry, its uses in book order and its additions.
[McpServerToolType]
public sealed class KbTools(McpRefs refs, IKbEntryQueries entries)
{
    // The entry window's use kinds in #20's words.
    private static readonly Dictionary<string, string> KindNames = new(StringComparer.Ordinal)
    {
        ["linked"] = "scene_link",
        ["mention"] = "mention",
        ["summary"] = "summary_mention",
        ["beat"] = "beat_mention",
        ["beat_context"] = "context",
        ["timeline"] = "addition",
        ["printed"] = "printed",
        ["text"] = "alias_match",
    };

    public sealed record Addition(Guid Id, McpItem Scene, string Text);

    public sealed record GetKbEntryResult(
        string Category, string Scope, string? ScopeTitle, IReadOnlyList<string> Aliases, string Summary, string Body,
        IReadOnlyDictionary<string, string> Fields, IReadOnlyList<KbCustomFieldDto> CustomFields, bool AlwaysInContext,
        IReadOnlyList<string> Tags, bool ExcludeFromAutoContext,
        IReadOnlyList<McpItem> Related, KbSourceDto? Source, IReadOnlyList<Addition> Additions, int UsedInScenes) : McpResult;

    public sealed record Usage(McpItem Item, string Book, IReadOnlyList<UsageKind> Kinds, string Snippet);

    public sealed record UsageKind(string Kind, int Count);

    public sealed record FindKbUsagesResult(IReadOnlyList<Usage> Uses, IReadOnlyDictionary<string, int> Counts, KbSourceDto? Source) : McpResult;

    [McpServerTool(Name = "get_kb_entry", Title = "Tell me about this entry", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("One Knowledge base entry: category, scope (book or series), aliases, tags, summary, body, fields, related entries, its source, " +
                 "and its additions (what the reader learns, scene by scene) in book order.")]
    public async Task<GetKbEntryResult> GetKbEntryAsync(
        McpServer server,
        [Description("The entry: id, name, alias or \"e:<slug>\".")] string entry,
        [Description("Look only in this book's scope (its own and its series' entries); default the whole Library.")] string? book = null,
        CancellationToken cancellationToken = default)
    {
        var scopeBook = book == null ? null : await refs.BookAsync(book, cancellationToken);
        var resolved = await refs.EntryAsync(entry, scopeBook, cancellationToken);
        var dto = await entries.GetAsync(resolved.Id, cancellationToken);
        var owner = await refs.AccessibleAsync(dto.BookId, cancellationToken);
        // Open it where it is used when nothing narrower was asked for; an unused series entry opens in a live book of
        // the series (McpEntry), never at the series id.
        var links = resolved with
        {
            LinkBookId = scopeBook?.Id ?? dto.Timeline.FirstOrDefault()?.BookId ?? dto.UsedIn.FirstOrDefault()?.BookId ?? resolved.LinkBookId,
        };

        var labels = await SceneLabelsAsync(server, dto.Timeline.Select(t => (t.BookId, t.SceneId)), cancellationToken);
        return new GetKbEntryResult(
            dto.CategoryName, owner?.IsSeries == true ? "series" : "book", owner?.Title, dto.Aliases, dto.Summary, dto.Body, dto.Fields,
            dto.CustomFields, dto.AlwaysInContext, dto.Tags, dto.ExcludeFromAutoContext,
            dto.Related.Select(r => new McpItem(r.Id, "kb_entry", r.Name, links.EntryUrl(r.Id))).ToList(),
            dto.Source,
            dto.Timeline.Select(t => new Addition(t.Id, labels[t.SceneId], t.Body)).ToList(),
            dto.UsedIn.Count)
        {
            Item = new McpItem(dto.Id, "kb_entry", dto.Name, links.EntryUrl(dto.Id)),
        };
    }

    [McpServerTool(Name = "find_kb_usages", Title = "Everywhere this is used", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Every scene that uses a Knowledge base entry, in book order, with how (scene_link, mention, summary_mention, beat_mention, " +
                 "context, addition, alias_match, printed), a snippet and an open_url that highlights the entry in Write; plus counts by kind " +
                 "and the source linked to the entry.")]
    public async Task<FindKbUsagesResult> FindKbUsagesAsync(
        McpServer server,
        [Description("The entry: id, name, alias or \"e:<slug>\".")] string entry,
        [Description("Only these kinds of use.")] string[]? kinds = null,
        [Description("For a series entry: only these books (ids or titles). Default every book.")] string[]? books = null,
        CancellationToken cancellationToken = default)
    {
        if (kinds?.FirstOrDefault(k => !KindNames.ContainsValue(k) && k != "source") is { } unknown)
        {
            throw McpToolException.Invalid("kinds", $"\"{unknown}\" is not one of {string.Join(", ", KindNames.Values)}, source.");
        }
        var bookIds = new List<Guid>();
        foreach (var book in books ?? [])
        {
            bookIds.Add((await refs.BookAsync(book, cancellationToken)).Id);
        }
        var resolved = await refs.EntryAsync(entry, null, cancellationToken);
        var dto = await entries.GetAsync(resolved.Id, cancellationToken);
        var labels = await SceneLabelsAsync(server, dto.UsedIn.Select(u => (u.BookId, u.SceneId)), cancellationToken);
        var titles = new Dictionary<Guid, string>();
        foreach (var bookId in dto.UsedIn.Select(u => u.BookId).Distinct())
        {
            titles[bookId] = (await refs.BookAsync(bookId.ToString(), cancellationToken)).Title;
        }

        var uses = dto.UsedIn
            .Where(u => bookIds.Count == 0 || bookIds.Contains(u.BookId))
            .Select(u => new Usage(
                labels[u.SceneId] with { OpenUrl = CodexLinks.Write(u.BookId, scene: u.Number, kb: dto.Id) },
                titles[u.BookId],
                u.Kinds.Select(k => new UsageKind(KindNames.GetValueOrDefault(k.Kind, k.Kind), k.Count))
                    .Where(k => kinds is not { Length: > 0 } || kinds.Contains(k.Kind)).ToList(),
                u.Snippet))
            .Where(u => u.Kinds.Count > 0)
            .ToList();
        var counts = uses.SelectMany(u => u.Kinds).GroupBy(k => k.Kind).ToDictionary(g => g.Key, g => g.Sum(k => k.Count));
        var source = kinds is not { Length: > 0 } || kinds.Contains("source") ? dto.Source : null;
        if (source != null)
        {
            counts["source"] = 1;
        }
        var links = resolved with { LinkBookId = dto.UsedIn.FirstOrDefault()?.BookId ?? resolved.LinkBookId };
        return new FindKbUsagesResult(uses, counts, source)
        {
            Item = new McpItem(dto.Id, "kb_entry", dto.Name, links.HighlightUrl(dto.Id)),
        };
    }

    // Scene items (label, number, open_url) for scenes across books, from each book's outline.
    private async Task<Dictionary<Guid, McpItem>> SceneLabelsAsync(
        McpServer server, IEnumerable<(Guid BookId, Guid SceneId)> scenes, CancellationToken cancellationToken)
    {
        var items = new Dictionary<Guid, McpItem>();
        foreach (var group in scenes.GroupBy(s => s.BookId))
        {
            var outline = await refs.OutlineAsync(await refs.BookAsync(group.Key.ToString(), cancellationToken), McpRefs.SessionKey(server), cancellationToken);
            foreach (var (_, sceneId) in group)
            {
                items[sceneId] = outline.SceneById(sceneId)?.Item(group.Key)
                                 ?? new McpItem(sceneId, "scene", "(scene not in the outline)", CodexLinks.Write(group.Key));
            }
        }
        return items;
    }
}
