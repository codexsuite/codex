using System.Text.RegularExpressions;
using Agoda.IoC.Core;

namespace Codex.Application.Mcp;

// Mentions in text an agent writes (#20 2.1): "@[Team Atlas Offsite]" becomes the stored mention "[Team Atlas Offsite](kb:<id>)"
// in scene summaries and beat instructions, the form the UI writes. The name is an EntryRef in the book's scope, so an
// unknown name fails the call (not_found), a name two entries share is ambiguous and an entry from another series is
// out_of_scope, all before anything is written. "§[Shipping Small]" becomes the chapter link "[Shipping Small](chapter:<id>)"
// (docs/prose-format.md) the same way: the name is a live chapter's title in the book, an unknown title is not_found and a
// title two chapters share is ambiguous. Given the book's outline, both kinds resolve; given only the book, "@[...]" does.
[RegisterSingleton]
public sealed partial class McpMentions(McpRefs refs)
{
    public async Task<string?> ResolveAsync(string? text, McpBook book, CancellationToken cancellationToken)
    {
        if (text == null || !text.Contains("@[", StringComparison.Ordinal))
        {
            return text;
        }
        var resolved = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (Match match in Mention().Matches(text))
        {
            var name = match.Groups["name"].Value.Trim();
            if (!resolved.ContainsKey(name))
            {
                resolved[name] = (await refs.EntryAsync(name, book, cancellationToken)).Id;
            }
        }
        return Mention().Replace(text, m => $"[{m.Groups["name"].Value.Trim()}](kb:{resolved[m.Groups["name"].Value.Trim()]})");
    }

    public async Task<string?> ResolveAsync(string? text, BookOutline outline, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(text, outline.Book, cancellationToken);
        if (resolved == null || !resolved.Contains("§[", StringComparison.Ordinal))
        {
            return resolved;
        }
        var chapters = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (Match match in ChapterLink().Matches(resolved))
        {
            var name = match.Groups["name"].Value.Trim();
            if (!chapters.ContainsKey(name))
            {
                chapters[name] = ChapterNamed(outline, name);
            }
        }
        return ChapterLink().Replace(resolved, m => $"[{m.Groups["name"].Value.Trim()}](chapter:{chapters[m.Groups["name"].Value.Trim()]})");
    }

    private static Guid ChapterNamed(BookOutline outline, string name)
    {
        var matches = outline.Chapters
            .Where(c => string.Equals(c.Dto.Title.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0].Dto.Id,
            0 => throw McpToolException.NotFound($"No chapter \"{name}\" in {outline.Book.Title}.",
                outline.Chapters.Where(c => c.Dto.Title.Contains(name, StringComparison.OrdinalIgnoreCase))
                    .Take(5).Select(c => c.Item(outline.Book.Id)).ToList()),
            _ => throw McpToolException.Ambiguous($"\"{name}\" names {matches.Count} chapters in {outline.Book.Title}; rename one or link it in Write.",
                matches.Select(c => c.Item(outline.Book.Id)).ToList()),
        };
    }

    [GeneratedRegex(@"@\[(?<name>[^\[\]]+)\](?!\()")]
    private static partial Regex Mention();

    [GeneratedRegex(@"§\[(?<name>[^\[\]]+)\](?!\()")]
    private static partial Regex ChapterLink();
}
