using System.Text.RegularExpressions;

namespace Codex.Application.Mcp;

// The labels an author reads: "Act 2 · The Product Engineer", "Ch 5 · Owning Outcomes",
// "Ch 5 · Sc 2 · Outcome metrics that don't rot". A scene is named by its title, else the first sentence of its
// summary (as plain text), cut at a word near 60 characters.
public static partial class McpLabels
{
    private const int MaxNameLength = 60;

    public static string Act(int number, string title) =>
        string.IsNullOrWhiteSpace(title) ? $"Act {number}" : $"Act {number} · {title}";

    public static string Chapter(int? number, string title) => number is { } n ? $"Ch {n} · {title}" : title;

    // number is the outline's display number: "5.2", or "2" in an unnumbered chapter.
    public static string Scene(string number, string chapterTitle, string? title, string? summary)
    {
        var dot = number.IndexOf('.', StringComparison.Ordinal);
        var place = dot < 0 ? $"{chapterTitle} · Sc {number}" : $"Ch {number[..dot]} · Sc {number[(dot + 1)..]}";
        var name = Name(title ?? summary);
        return name.Length == 0 ? place : $"{place} · {name}";
    }

    // Markdown to one line of plain text: link text kept, emphasis and code marks dropped.
    public static string Plain(string? markdown) =>
        Whitespace().Replace(Marks().Replace(Link().Replace(markdown ?? "", "$1"), ""), " ").Trim();

    private static string Name(string? markdown)
    {
        var text = Plain(markdown);
        var sentenceEnd = SentenceEnd().Match(text);
        if (sentenceEnd.Success)
        {
            text = text[..sentenceEnd.Index];
        }
        text = text.TrimEnd('.', ' ');
        if (text.Length <= MaxNameLength)
        {
            return text;
        }
        var cut = text.LastIndexOf(' ', MaxNameLength);
        return text[..(cut > 0 ? cut : MaxNameLength)].TrimEnd(',', ';', ':', ' ') + "…";
    }

    [GeneratedRegex(@"\[((?:\\.|[^\[\]\\])*)\]\((?:kb|chapter|ref):[^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"[*_`]|\\(?=.)")]
    private static partial Regex Marks();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[.!?](?=\s)")]
    private static partial Regex SentenceEnd();
}
