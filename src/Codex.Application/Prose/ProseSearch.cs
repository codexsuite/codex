using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Application.Prose;

// Find and replace over the Markdown a scene is stored as (docs/prose-format.md). It searches what an author reads:
// the text of paragraphs, headings, quotes and lists, and the label of a link. It never looks at a `::` directive
// line, a container's opening line, a fenced block or a link's target, and never changes the words of a `ref:`
// passage (they are the quoted or paraphrased text) or of a `chapter:` link. A mention's label (`kb:`) is replaced
// and its target kept. A match that runs across a link boundary is reported and skipped. Matching is on the visible
// text (escapes read), replacing is on the source (the replacement is escaped).
public static partial class ProseSearch
{
    private const int Around = 32;

    private const string Escapable = "\\*_`[]";

    // The triggers' link regex: link text holds no unescaped brackets.
    [GeneratedRegex(@"\[((?:[^\[\]\\]|\\.)*)\]\((ref|kb|chapter):([0-9a-fA-F-]{36})\)")]
    private static partial Regex LinkPattern();

    private sealed record Segment(int Index, string? LinkKind);

    private sealed class Visible
    {
        internal StringBuilder Text { get; } = new();

        // For each visible character: where it is in the source, and the run of text (segment) it belongs to.
        internal List<int> RawStart { get; } = [];

        internal List<int> RawEnd { get; } = [];

        internal List<Segment> Segment { get; } = [];
    }

    private sealed record Hit(TextMatch Match, int RawStart, int RawEnd);

    public static IReadOnlyList<TextMatch> Find(string text, SearchKind kind, SearchOptions options) =>
        Scan(text, kind, options).Select(h => h.Match).ToList();

    // Replaces every replaceable match, or only the one with `onlyOrdinal`. Cited and spanning matches are counted, not
    // changed. `Text` is the input when nothing was replaced.
    public static ReplaceOutcome Replace(string text, SearchKind kind, SearchOptions options, string replacement, int? onlyOrdinal = null)
    {
        var hits = Scan(text, kind, options);
        var literal = kind == SearchKind.Plain ? replacement : Escape(replacement.ReplaceLineEndings(" "));
        var output = new StringBuilder(text);
        var replaced = 0;
        for (var at = hits.Count - 1; at >= 0; at--)
        {
            var hit = hits[at];
            if (onlyOrdinal is { } only && hit.Match.Ordinal != only)
            {
                continue;
            }

            if (hit.Match.Status != MatchStatus.Replaceable)
            {
                continue;
            }

            output.Remove(hit.RawStart, hit.RawEnd - hit.RawStart).Insert(hit.RawStart, literal);
            replaced++;
        }

        var skipped = hits.Where(h => onlyOrdinal is null || h.Match.Ordinal == onlyOrdinal);
        return new ReplaceOutcome(
            replaced == 0 ? text : output.ToString(),
            replaced,
            skipped.Count(h => h.Match.Status == MatchStatus.Cited),
            skipped.Count(h => h.Match.Status == MatchStatus.SpansLink));
    }

    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Escapable.Contains(c, StringComparison.Ordinal))
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }

    private static List<Hit> Scan(string text, SearchKind kind, SearchOptions options)
    {
        var hits = new List<Hit>();
        if (options.Query.Length == 0 || text.Length == 0)
        {
            return hits;
        }

        var pattern = Pattern(options);
        var ordinal = 0;
        var inFence = false;
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                end = text.Length;
            }

            var line = text.AsSpan(start, end - start);
            if (kind != SearchKind.Prose || Searchable(line, ref inFence))
            {
                var visible = kind == SearchKind.Plain ? PlainLine(text, start, end) : ParseLine(text, start, end);
                foreach (Match found in pattern.Matches(visible.Text.ToString()))
                {
                    if (found.Length == 0)
                    {
                        continue;
                    }

                    hits.Add(ToHit(visible, found, ordinal++));
                }
            }

            start = end + 1;
        }

        return hits;
    }

    // Fenced blocks (including raw LaTeX), `::` directive lines and `:::` container lines hold nothing to search.
    private static bool Searchable(ReadOnlySpan<char> line, ref bool inFence)
    {
        if (line.StartsWith("```", StringComparison.Ordinal))
        {
            inFence = !inFence || line.Length > 3;
            return false;
        }

        return !inFence && !line.StartsWith("::", StringComparison.Ordinal);
    }

    private static Regex Pattern(SearchOptions options)
    {
        var body = Regex.Escape(options.Query);
        if (options.WholeWord)
        {
            body = $@"(?<![\p{{L}}\p{{N}}_]){body}(?![\p{{L}}\p{{N}}_])";
        }

        var flags = RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new Regex(body, flags, TimeSpan.FromSeconds(2));
    }

    private static Visible PlainLine(string text, int start, int end)
    {
        var visible = new Visible();
        var segment = new Segment(0, null);
        for (var i = start; i < end; i++)
        {
            visible.Text.Append(text[i]);
            visible.RawStart.Add(i);
            visible.RawEnd.Add(i + 1);
            visible.Segment.Add(segment);
        }

        return visible;
    }

    // The visible text of one source line: runs of text and the labels of links, escapes read.
    private static Visible ParseLine(string text, int start, int end)
    {
        var visible = new Visible();
        var segments = 0;
        var at = start;
        foreach (Match link in LinkPattern().Matches(text.Substring(start, end - start)))
        {
            var linkStart = start + link.Index;
            AddRun(visible, text, at, linkStart, new Segment(segments++, null));
            var label = link.Groups[1];
            AddRun(visible, text, start + label.Index, start + label.Index + label.Length, new Segment(segments++, link.Groups[2].Value));
            at = linkStart + link.Length;
        }

        AddRun(visible, text, at, end, new Segment(segments, null));
        return visible;
    }

    private static void AddRun(Visible visible, string text, int from, int to, Segment segment)
    {
        var i = from;
        while (i < to)
        {
            var width = text[i] == '\\' && i + 1 < to && Escapable.Contains(text[i + 1], StringComparison.Ordinal) ? 2 : 1;
            visible.Text.Append(text[i + width - 1]);
            visible.RawStart.Add(i);
            visible.RawEnd.Add(i + width);
            visible.Segment.Add(segment);
            i += width;
        }
    }

    private static Hit ToHit(Visible visible, Match found, int ordinal)
    {
        var first = visible.Segment[found.Index];
        var last = visible.Segment[found.Index + found.Length - 1];
        var status = MatchStatus.Replaceable;
        if (first.Index != last.Index)
        {
            status = MatchStatus.SpansLink;
        }
        else if (first.LinkKind is "ref" or "chapter")
        {
            status = MatchStatus.Cited;
        }

        var all = visible.Text.ToString();
        var before = all[Math.Max(0, found.Index - Around)..found.Index];
        var after = all[(found.Index + found.Length)..Math.Min(all.Length, found.Index + found.Length + Around)];
        return new Hit(
            new TextMatch(ordinal, status, before, found.Value, after),
            visible.RawStart[found.Index],
            visible.RawEnd[found.Index + found.Length - 1]);
    }
}
