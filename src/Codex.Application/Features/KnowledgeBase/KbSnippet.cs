using System.Text.RegularExpressions;

namespace Codex.Application.Features.KnowledgeBase;

// Picks the sentence that best shows an entry in use, as plain text: the first sentence (prose first, then the
// scene summary) that links the entry or holds its name or an alias. Directive lines (`::beat`, `::kb-addition`)
// are skipped. Also tells whether the name appears as plain text, outside any link.
public static partial class KbSnippet
{
    public static string Find(Guid entryId, IReadOnlyList<string> names, string body, string summary)
    {
        var token = $"kb:{entryId}";
        var nameRegex = NameRegex(names);
        foreach (var text in new[] { body, summary })
        {
            foreach (var paragraph in Paragraphs(text))
            {
                if (!paragraph.Contains(token, StringComparison.OrdinalIgnoreCase) && !nameRegex.IsMatch(Plain(paragraph)))
                {
                    continue;
                }
                var sentence = SentenceSplit().Split(paragraph)
                    .FirstOrDefault(s => s.Contains(token, StringComparison.OrdinalIgnoreCase) || nameRegex.IsMatch(Plain(s))) ?? paragraph;
                return Plain(sentence).Trim();
            }
        }
        return Plain(summary).Trim();
    }

    // True when the name or an alias occurs in the prose or summary outside a link.
    public static bool HasPlainName(IReadOnlyList<string> names, string body, string summary)
    {
        var nameRegex = NameRegex(names);
        return Paragraphs(body).Concat(Paragraphs(summary)).Any(p => nameRegex.IsMatch(Unlinked(p)));
    }

    private static IEnumerable<string> Paragraphs(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("::", StringComparison.Ordinal)
                                                          && !l.StartsWith(":::", StringComparison.Ordinal));

    private static Regex NameRegex(IReadOnlyList<string> names) =>
        new($@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', names.Where(n => n.Length > 0).Select(Regex.Escape))})(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string Unlinked(string text) => LinkPattern().Replace(text, " ");

    private static string Plain(string text) => Emphasis().Replace(LinkPattern2().Replace(text, "$1"), "");

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z\[])")]
    private static partial Regex SentenceSplit();

    [GeneratedRegex(@"\[[^\]]*\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern2();

    [GeneratedRegex(@"[*_`]|^>\s*|^#+\s*")]
    private static partial Regex Emphasis();
}
