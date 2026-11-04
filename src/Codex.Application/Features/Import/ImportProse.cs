using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Application.Features.Import;

// Turns an author's Markdown into the stored prose format (docs/prose-format.md): serialize(parse(x)) == x for the
// output, and no imported text becomes an embed or a directive in either reader.
// - Blocks: LF line ends, no trailing spaces or NULs, one blank line between blocks, a final "\n". Headings, scene
//   breaks ("* * *", "---", "***"; the format has no break node, so they are stored as the escaped literal "\* \* \*"),
//   quotes, lists ("*"/"+" bullets as "-", ordered lists numbered up from the first) and fences are their own blocks.
// - Every paragraph, list item and quote line that would start another block is escaped as the TS serializer does,
//   and "::" anywhere at a line start (also after a list or quote marker) gets a backslash.
// - Inline: "_x_"/"__x__" become "*x*"/"**x**"; a "*" run is kept only where it pairs cleanly with a run of the same
//   length (else escaped); literal "_", "[", "]", unpaired backticks and stray backslashes are escaped; links to kb:,
//   ref: or chapter: targets (rows a new book does not have) become their text.
// - Code (fences and spans) keeps its text, except that the patterns a reader or trigger finds even there are broken
//   with a space: a fence line starting "::", and "](ref:", "](kb:" or "](chapter:".
// - Fence, Table and Opaque build the blocks a source's mapping emits itself (directive lines, tables, raw LaTeX,
//   kept HTML comments); the caller joins them with ToStored's blocks, so they are never escaped by it.
public static partial class ImportProse
{
    public const string SceneBreak = "\\* \\* \\*";

    private const string Escapable = "\\*_`[]";

    public static string ToStored(string markdown)
    {
        var lines = Normalize(markdown).Split('\n');
        var blocks = new List<string>();
        var current = new List<string>();
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i++];
            if (IsFence(line))
            {
                Render(current, blocks);
                var fence = new List<string> { line.TrimStart() };
                while (i < lines.Length)
                {
                    var inside = lines[i++];
                    if (inside == "```")
                    {
                        fence.Add(inside);
                        break;
                    }
                    fence.Add(NeutraliseCode(inside.StartsWith("::", StringComparison.Ordinal) ? " " + inside : inside));
                }
                blocks.Add(string.Join('\n', fence).TrimEnd('\n'));
            }
            else if (line.Trim().Length == 0)
            {
                Render(current, blocks);
            }
            else
            {
                current.Add(line);
            }
        }
        Render(current, blocks);
        return blocks.Count == 0 ? "" : string.Join("\n\n", blocks) + "\n";
    }

    // Blocks a source's mapping emits around ToStored's output (docs/import.md, pandoc). They are written as given,
    // never escaped, but neutralised like code: a line a reader or trigger would read as an embed gets a leading space,
    // and "](ref:", "](kb:" and "](chapter:" are broken, so imported text still never becomes an embed.

    // ```{=latex} … ``` (info "{=latex}"), or another fence. A body line that is exactly ``` would close it early.
    public static string Fence(string info, string body)
    {
        var lines = Normalize(body).Trim('\n').Split('\n')
            .Select(l => l == "```" || l.StartsWith("::", StringComparison.Ordinal) ? " " + l : l)
            .Select(NeutraliseCode);
        return "```" + info + "\n" + string.Join('\n', lines) + "\n```";
    }

    // A pipe table, its rows as written (cells keep their inline Markdown); links to app targets become their text.
    public static string Table(IEnumerable<string> rows) =>
        string.Join('\n', rows.Select(r => AppLink().Replace(r.Trim(), m => m.Groups[1].Value)).Select(r => r.StartsWith("::", StringComparison.Ordinal) ? "\\" + r : r));

    // An unknown container (":::name" … ":::"): an opaque block both readers keep whole and print never shows.
    public static string Opaque(string name, string raw)
    {
        var lines = Normalize(raw).Trim('\n').Split('\n')
            .Select(l => l.StartsWith("::", StringComparison.Ordinal) ? " " + l : l)
            .Select(NeutraliseCode);
        return ":::" + name + "\n" + string.Join('\n', lines) + "\n:::";
    }

    private static string Normalize(string markdown)
    {
        var text = markdown.TrimStart('\uFEFF').Replace("\0", "").Replace("\r\n", "\n").Replace('\r', '\n');
        return string.Join('\n', text.Split('\n').Select(l => l.TrimEnd(' ', '\t')));
    }

    private static bool IsFence(string line) => line.TrimStart(' ').StartsWith("```", StringComparison.Ordinal) && line.Length - line.TrimStart(' ').Length <= 3;

    // One run of non-blank lines, cut into the blocks the TS reader would see.
    private static void Render(List<string> lines, List<string> blocks)
    {
        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (SceneBreakLine().IsMatch(line))
            {
                blocks.Add(SceneBreak);
                i++;
            }
            else if (HeadingLine().Match(line) is { Success: true } heading)
            {
                blocks.Add(heading.Groups[1].Value + " " + Inline(heading.Groups[2].Value.Trim()));
                i++;
            }
            else if (QuoteLine().IsMatch(line))
            {
                var inner = new List<string>();
                while (i < lines.Count && QuoteLine().IsMatch(lines[i]))
                {
                    inner.Add(QuoteLine().Replace(lines[i++], "", 1));
                }
                AddIfAny(blocks, Quote(inner));
            }
            else if (Item(line) is { } first)
            {
                var items = new List<(string Marker, List<string> Lines)>();
                var number = first.Number;
                while (i < lines.Count)
                {
                    if (Item(lines[i]) is { } item && item.Ordered == first.Ordered)
                    {
                        items.Add((first.Ordered ? $"{number++}." : "-", [item.Text]));
                        i++;
                    }
                    else if (lines[i].StartsWith(' ') && !SceneBreakLine().IsMatch(lines[i]) && Item(lines[i]) == null)
                    {
                        items[^1].Lines.Add(lines[i++].Trim());
                    }
                    else
                    {
                        break;
                    }
                }
                blocks.Add(string.Join('\n', items.Select(item => ListItem(item.Marker, item.Lines))));
            }
            else
            {
                var paragraph = new List<string>();
                while (i < lines.Count && (paragraph.Count == 0 || !Interrupts(lines[i])))
                {
                    paragraph.Add(lines[i++].Trim());
                }
                AddIfAny(blocks, Paragraph(paragraph));
            }
        }
        lines.Clear();
    }

    // CommonMark's interruptions of a paragraph: a heading, a break, a quote, a bullet or an ordered item starting at 1.
    private static bool Interrupts(string line) =>
        SceneBreakLine().IsMatch(line) || HeadingLine().IsMatch(line) || QuoteLine().IsMatch(line)
        || Item(line) is { Ordered: false } or { Ordered: true, Number: 1 };

    private static string Paragraph(List<string> lines)
    {
        // Inline per line: the TS serializer closes marks at a soft break, so emphasis never spans one.
        return string.Join('\n', lines.Select((l, n) => EscapeStart(Inline(UnescapeStart(l)), n == 0)));
    }

    // A quote's lines as paragraphs (a "> " paragraph is the only shape it keeps), split at its empty lines.
    private static string Quote(List<string> inner)
    {
        var paragraphs = new List<string>();
        var current = new List<string>();
        foreach (var line in inner.Append(""))
        {
            if (line.Trim().Length > 0)
            {
                current.Add(line.Trim());
                continue;
            }
            if (current.Count > 0)
            {
                paragraphs.Add(Paragraph(current));
                current = [];
            }
        }
        return string.Join("\n>\n", paragraphs.Select(p => string.Join('\n', p.Split('\n').Select(l => "> " + l))));
    }

    // An item holds one paragraph: its first line and any indented lines under it, re-indented to the marker width.
    private static string ListItem(string marker, List<string> lines)
    {
        var body = Paragraph(lines);
        if (body.Length == 0)
        {
            return marker + " " + EscapeStart("", true);
        }
        var pad = new string(' ', marker.Length + 1);
        return string.Join('\n', body.Split('\n').Select((l, n) => n == 0 ? marker + " " + l : pad + l));
    }

    private static void AddIfAny(List<string> blocks, string block)
    {
        if (block.Length > 0)
        {
            blocks.Add(block);
        }
    }

    // A source "\#", "\>" or "\::" at a line start was an escape; EscapeStart puts back the one the format needs.
    private static string UnescapeStart(string line)
    {
        if (line.Length > 1 && line[0] == '\\' && ":#>-+".Contains(line[1]))
        {
            return line[1..];
        }
        var ordered = EscapedOrderedStart().Match(line);
        return ordered.Success ? ordered.Groups[1].Value + line[(ordered.Groups[1].Length + 1)..] : line;
    }

    // As the TS serializer: "::" on any line, and on a block's first line "#", ">", "-" or "N." that would start a block.
    private static string EscapeStart(string line, bool first)
    {
        if (line.StartsWith("::", StringComparison.Ordinal))
        {
            return "\\" + line;
        }
        if (!first)
        {
            return line;
        }
        if (BlockStart().IsMatch(line) || line.StartsWith('>'))
        {
            return "\\" + line;
        }
        var ordered = OrderedStart().Match(line);
        return ordered.Success ? $"{ordered.Groups[1].Value}\\{line[ordered.Groups[1].Length..]}" : line;
    }

    private static (bool Ordered, int Number, string Text)? Item(string line)
    {
        var bullet = BulletLine().Match(line);
        if (bullet.Success)
        {
            return (false, 0, bullet.Groups[1].Value);
        }
        var ordered = OrderedLine().Match(line);
        return ordered.Success && int.TryParse(ordered.Groups[1].Value, out var n) ? (true, n, ordered.Groups[2].Value) : null;
    }

    private static string NeutraliseCode(string code) => CodeLink().Replace(code, "] ($1:");

    // Inline text: code spans are atoms, star runs are paired, everything else is escaped where the format needs it.
    private static string Inline(string text)
    {
        var pieces = new List<Piece>();
        var plain = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '`')
            {
                plain.Append("\\`");
                i += 2;
            }
            else if (c == '`')
            {
                var run = RunLength(text, i, '`');
                var close = FindClosingRun(text, i + run, run);
                var code = close < 0 ? "" : text[(i + run)..close];
                var fenceAtStart = i == 0 && CodeSpan(code).StartsWith("```", StringComparison.Ordinal);
                if (code.Length == 0 || code.Contains('\n') || code[0] == '`' || code[^1] == '`'
                    || char.IsWhiteSpace(code[0]) || char.IsWhiteSpace(code[^1]) || fenceAtStart)
                {
                    plain.Append(string.Concat(Enumerable.Repeat("\\`", run)));
                    i += run;
                    continue;
                }
                FlushText(plain, pieces);
                pieces.Add(new Piece(CodeSpan(NeutraliseCode(code)), null, true));
                i = close + run;
            }
            else
            {
                plain.Append(c);
                i++;
            }
        }
        FlushText(plain, pieces);
        return Emphasis(pieces);
    }

    private static void FlushText(StringBuilder plain, List<Piece> pieces)
    {
        if (plain.Length == 0)
        {
            return;
        }
        var unlinked = AppLink().Replace(plain.ToString(), m => m.Groups[1].Value);
        var starred = Underscores().Replace(unlinked, m => new string('*', m.Groups[1].Length) + m.Groups[2].Value + new string('*', m.Groups[1].Length));
        var literal = new StringBuilder();
        var i = 0;
        while (i < starred.Length)
        {
            var c = starred[i];
            if (c == '\\' && i + 1 < starred.Length && Escapable.Contains(starred[i + 1]))
            {
                literal.Append(c).Append(starred[i + 1]);
                i += 2;
            }
            else if (c == '*')
            {
                if (literal.Length > 0)
                {
                    pieces.Add(new Piece(literal.ToString(), null, false));
                    literal.Clear();
                }
                var run = RunLength(starred, i, '*');
                pieces.Add(new Piece("", run, false));
                i += run;
            }
            else
            {
                literal.Append(c is '\\' or '[' or ']' or '_' or '`' ? "\\" + c : c.ToString());
                i++;
            }
        }
        if (literal.Length > 0)
        {
            pieces.Add(new Piece(literal.ToString(), null, false));
        }
        plain.Clear();
    }

    // The format's whitespace-only flanking rule (docs/prose-format.md): a run opens when the next character is not
    // whitespace and closes when the previous one is not, whatever the other side is ("*Ship it*—the", "un**believ**able"). A closer pairs with the nearest open run of the same length (1 to 3); openers it
    // passes over, and every run left unpaired, are written escaped.
    private static string Emphasis(List<Piece> pieces)
    {
        var paired = new bool[pieces.Count];
        var open = new List<int>();
        for (var p = 0; p < pieces.Count; p++)
        {
            if (pieces[p].Stars is not { } length || length > 3)
            {
                continue;
            }
            var before = CharBefore(pieces, p);
            var after = CharAfter(pieces, p);
            var canOpen = after is { } a && !char.IsWhiteSpace(a) && a != '*';
            var canClose = before is { } b && !char.IsWhiteSpace(b) && b != '*';
            var match = canClose ? open.FindLastIndex(o => pieces[o].Stars == length) : -1;
            if (match >= 0)
            {
                paired[open[match]] = true;
                paired[p] = true;
                open.RemoveRange(match, open.Count - match);
            }
            else if (canOpen)
            {
                open.Add(p);
            }
        }
        var output = new StringBuilder();
        for (var p = 0; p < pieces.Count; p++)
        {
            var piece = pieces[p];
            if (piece.Stars is { } stars)
            {
                output.Append(paired[p] ? new string('*', stars) : string.Concat(Enumerable.Repeat("\\*", stars)));
            }
            else
            {
                output.Append(piece.Text);
            }
        }
        return output.ToString();
    }

    private static char? CharBefore(List<Piece> pieces, int index)
    {
        for (var p = index - 1; p >= 0; p--)
        {
            if (pieces[p].Stars != null)
            {
                return '*';
            }
            if (pieces[p].Text.Length > 0)
            {
                return pieces[p].IsCode ? 'x' : pieces[p].Text[^1];
            }
        }
        return null;
    }

    private static char? CharAfter(List<Piece> pieces, int index)
    {
        for (var p = index + 1; p < pieces.Count; p++)
        {
            if (pieces[p].Stars != null)
            {
                return '*';
            }
            if (pieces[p].Text.Length > 0)
            {
                return pieces[p].IsCode ? 'x' : pieces[p].Text[0];
            }
        }
        return null;
    }

    // As the TS serializer writes a code span: a fence one longer than the longest backtick run inside.
    private static string CodeSpan(string code)
    {
        var longest = 0;
        for (var i = 0; i < code.Length; i++)
        {
            longest = Math.Max(longest, code[i] == '`' ? RunLength(code, i, '`') : 0);
        }
        var fence = new string('`', longest + 1);
        return fence + code + fence;
    }

    private static int RunLength(string text, int start, char c)
    {
        var end = start;
        while (end < text.Length && text[end] == c)
        {
            end++;
        }
        return end - start;
    }

    private static int FindClosingRun(string text, int from, int run)
    {
        var at = from;
        while ((at = text.IndexOf('`', at)) >= 0)
        {
            var length = RunLength(text, at, '`');
            if (length == run)
            {
                return at;
            }
            at += length;
        }
        return -1;
    }

    private sealed record Piece(string Text, int? Stars, bool IsCode);

    [GeneratedRegex(@"^ {0,3}([-*_])(?: *\1){2,} *$")]
    private static partial Regex SceneBreakLine();

    [GeneratedRegex(@"^ {0,3}(#{1,6}) +(\S.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^ {0,3}> ?")]
    private static partial Regex QuoteLine();

    [GeneratedRegex(@"^ {0,3}[*+-] +(\S.*)$")]
    private static partial Regex BulletLine();

    [GeneratedRegex(@"^ {0,3}(\d{1,9})[.)] +(\S.*)$")]
    private static partial Regex OrderedLine();

    [GeneratedRegex(@"^(#{1,6}|>|-)(?: |$)")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^(\d+)\.(?: |$)")]
    private static partial Regex OrderedStart();

    [GeneratedRegex(@"\]\((ref|kb|chapter):")]
    private static partial Regex CodeLink();

    [GeneratedRegex(@"^(\d+)\\[.)]")]
    private static partial Regex EscapedOrderedStart();

    [GeneratedRegex(@"\[([^\[\]\\]*)\]\((?:kb|ref|chapter):[^)\s]*\)")]
    private static partial Regex AppLink();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_\\])(__|_)(?=\S)([^_*`]+?)(?<=\S)\1(?![\p{L}\p{N}_])")]
    private static partial Regex Underscores();
}
