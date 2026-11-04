using System.Text;
using System.Text.RegularExpressions;
using Codex.Application.Prose;

namespace Codex.Application.Features.Citations;

// Finds a quote flag's words in the stored Markdown. The scanner (References' QuoteFinder) keeps the words as plain
// text: link labels without their brackets, no emphasis stars, no escapes, no block-quote carets, whitespace collapsed.
// So each paragraph-like block is read the same way, keeping where every plain character came from, the excerpt is
// found in that plain text, and its range is mapped back. An occurrence between quotation marks (the one the scanner
// flagged) wins over any other; then the range is everything between the marks, emphasis included.
public static partial class FlagPassage
{
    private const string Opening = "\"“";
    private const string Closing = "\"”";

    /// <summary>The Markdown range of the excerpt in `body`, or null when it is not there.</summary>
    public static (int Start, int End)? Locate(string body, string excerpt)
    {
        var wanted = Whitespace().Replace(excerpt, " ").Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        (int Start, int End)? first = null;
        foreach (var block in ProseDocument.Parse(body).Blocks.Where(b => b.Kind is ProseBlockKind.Paragraph or ProseBlockKind.Quote or ProseBlockKind.List))
        {
            var (plain, map) = Project(body, block.Start, block.End);
            for (var at = plain.IndexOf(wanted, StringComparison.Ordinal); at >= 0; at = plain.IndexOf(wanted, at + 1, StringComparison.Ordinal))
            {
                var after = at + wanted.Length;
                if (at > 0 && after < plain.Length && Opening.Contains(plain[at - 1], StringComparison.Ordinal) && Closing.Contains(plain[after], StringComparison.Ordinal))
                {
                    return (map[at - 1] + 1, map[after]);
                }

                first ??= Widen(body, map[at], map[after - 1] + 1, block.Start, block.End);
            }
        }

        return first;
    }

    // An unquoted match takes the emphasis markers right around it, so `*words*` is wrapped whole.
    private static (int Start, int End) Widen(string body, int start, int end, int blockStart, int blockEnd)
    {
        while (start > blockStart && body[start - 1] is '*' or '_')
        {
            start--;
        }

        while (end < blockEnd && body[end] is '*' or '_')
        {
            end++;
        }

        return (start, end);
    }

    private static (string Plain, List<int> Map) Project(string body, int start, int end)
    {
        var skip = new bool[end - start];
        void Skip(int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                skip[i - start] = true;
            }
        }

        var text = body[start..end];
        foreach (Match link in Link().Matches(text))
        {
            var label = link.Groups[1];
            Skip(start + link.Index, start + label.Index);
            Skip(start + label.Index + label.Length, start + link.Index + link.Length);
        }

        foreach (Match caret in Caret().Matches(text))
        {
            Skip(start + caret.Index, start + caret.Index + caret.Length);
        }

        var escaped = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (escaped)
            {
                // The escaped character stays, except a star: QuoteFinder unescapes and then drops every star.
                skip[i] |= text[i] == '*';
                escaped = false;
            }
            else if (text[i] == '\\' && i + 1 < text.Length)
            {
                skip[i] = true;
                escaped = true;
            }
            else if (text[i] == '*')
            {
                skip[i] = true;
            }
        }

        var plain = new StringBuilder();
        var map = new List<int>();
        for (var i = 0; i < text.Length; i++)
        {
            if (skip[i])
            {
                continue;
            }

            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (plain.Length > 0 && plain[^1] == ' ')
                {
                    continue;
                }

                c = ' ';
            }

            plain.Append(c);
            map.Add(start + i);
        }

        return (plain.ToString(), map);
    }

    [GeneratedRegex(@"\[((?:[^\[\]\\]|\\.)*)\]\([a-z]+:[^)\s]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?m)^>[ ]?")]
    private static partial Regex Caret();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
