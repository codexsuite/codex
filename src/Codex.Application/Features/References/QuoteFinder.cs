using System.Text.RegularExpressions;
using Codex.Application.Prose;

namespace Codex.Application.Features.References;

// Finds text in quotation marks that no source marker covers: the quoted passages of prose a reader would take for
// someone else's words. A quote is ignored when it sits inside a `ref:` link, holds one, is followed by a bare
// `[^](ref:…)` marker, or has fewer than MinimumWords words.
public static partial class QuoteFinder
{
    public const int MinimumWords = 4;

    public static IReadOnlyList<string> Find(string body)
    {
        var found = new List<string>();
        foreach (var block in ProseDocument.Parse(body).Blocks.Where(IsText))
        {
            var text = body[block.Start..block.End];
            var links = RefLink().Matches(text).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
            foreach (Match quote in Quoted().Matches(text))
            {
                var end = quote.Index + quote.Length;
                if (links.Any(l => l.Index < end && quote.Index < l.End) || MarkerFollows(text, end))
                {
                    continue;
                }

                var excerpt = Plain(quote.Groups[1].Success ? quote.Groups[1].Value : quote.Groups[2].Value);
                if (excerpt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinimumWords && !found.Contains(excerpt, StringComparer.Ordinal))
                {
                    found.Add(excerpt);
                }
            }
        }

        return found;
    }

    private static bool IsText(ProseBlock block) => block.Kind is ProseBlockKind.Paragraph or ProseBlockKind.Quote or ProseBlockKind.List;

    private static bool MarkerFollows(string text, int end)
    {
        var rest = text.AsSpan(end);
        while (!rest.IsEmpty && ".,;:!?)".Contains(rest[0], StringComparison.Ordinal))
        {
            rest = rest[1..];
        }

        return rest.StartsWith("[^](ref:", StringComparison.Ordinal);
    }

    // Markdown out, plain words in: links keep their label, emphasis and escapes go, block-quote carets go, space collapses.
    private static string Plain(string markdown)
    {
        var text = Link().Replace(markdown, "$1");
        text = Caret().Replace(text, string.Empty);
        text = Escape().Replace(text, "$1").Replace("*", string.Empty, StringComparison.Ordinal);
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex("\"([^\"]+)\"|“([^”]+)”")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\[((?:[^\[\]\\]|\\.)*)\]\(ref:[0-9a-fA-F-]{36}\)")]
    private static partial Regex RefLink();

    [GeneratedRegex(@"\[((?:[^\[\]\\]|\\.)*)\]\([a-z]+:[^)\s]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?m)^>[ ]?")]
    private static partial Regex Caret();

    [GeneratedRegex(@"\\(.)")]
    private static partial Regex Escape();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
