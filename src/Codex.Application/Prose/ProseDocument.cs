using System.Text;
using System.Text.RegularExpressions;

namespace Codex.Application.Prose;

// A scene body in the stored prose format (issue #10). Pure: no database. Every edit returns a new document and
// leaves every byte outside the edited span exactly as it was. ToString() returns the original text.
//
// Unknown ids: InsertAfterBeat, UnwrapRef and StripKbAddition throw ProseEditException(TargetNotFound).
// ReplaceMentionWithText returns the document unchanged when the entry has no mention (a body may have none).
public sealed partial class ProseDocument
{
    private const string UuidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    [GeneratedRegex("^::(?<name>beat|kb-addition|figure)\\{id=\"(?<id>" + UuidPattern + ")\"\\}$")]
    private static partial Regex IdDirective();

    [GeneratedRegex("^::(?<name>spell|statblock)\\{kb=\"(?<id>" + UuidPattern + ")\"\\}$")]
    private static partial Regex KbDirective();

    [GeneratedRegex("^::pagebreak$")]
    private static partial Regex PageBreak();

    // Link text may hold escaped characters and any other character except an unescaped bracket.
    [GeneratedRegex("\\[(?<text>(?:\\\\.|[^\\[\\]\\\\])*)\\]\\((?<scheme>kb|chapter|ref):(?<id>" + UuidPattern + ")\\)")]
    private static partial Regex InlineLink();

    [GeneratedRegex("(?<!\\\\)[\\[\\]]")]
    private static partial Regex UnescapedBracket();

    // A span opens with a run of backticks and closes with a run of the same length, so a shorter run inside is text.
    [GeneratedRegex("(?<!`)(`+)(?!`)[^\\n]+?(?<!`)\\1(?!`)")]
    private static partial Regex CodeSpan();

    private readonly Lazy<IReadOnlyList<ProseBlock>> _blocks;

    private ProseDocument(string text)
    {
        Text = text;
        _blocks = new Lazy<IReadOnlyList<ProseBlock>>(() => ParseBlocks(text));
    }

    public string Text { get; }

    public IReadOnlyList<ProseBlock> Blocks => _blocks.Value;

    public static ProseDocument Parse(string text) => new(text);

    public override string ToString() => Text;

    public IReadOnlyList<ProseEmbed> ListEmbeds()
    {
        var embeds = new List<ProseEmbed>();
        foreach (var block in Blocks)
        {
            switch (block.Kind)
            {
                case ProseBlockKind.Directive when block.Id is { } id:
                    embeds.Add(new ProseEmbed(DirectiveKind(block.Directive!), id, block.Start, block.End - block.Start, null));
                    break;
                case ProseBlockKind.Paragraph or ProseBlockKind.Heading or ProseBlockKind.Quote or ProseBlockKind.List or ProseBlockKind.Table:
                    foreach (var link in InlineLinks(block))
                    {
                        embeds.Add(new ProseEmbed(LinkKind(link.Scheme), link.Id, link.Start, link.Length, link.Text));
                    }

                    break;
            }
        }

        return embeds;
    }

    // Inserts a block straight after the beat's line, separated by one blank line from the beat and from whatever
    // followed it. `markdown` is one or more whole blocks; trailing newlines are ignored.
    public ProseDocument InsertAfterBeat(Guid beatId, string markdown)
    {
        var insert = markdown.TrimEnd('\n');
        if (insert.Length == 0)
        {
            throw new ProseEditException(ProseEditFailure.EmptyInsert, "Nothing to insert.");
        }

        var beat = Blocks.FirstOrDefault(b => b.Directive == "beat" && b.Id == beatId)
            ?? throw new ProseEditException(ProseEditFailure.TargetNotFound, $"No beat {beatId} in the body.");
        if (beat.End == Text.Length)
        {
            return new ProseDocument(Text + "\n\n" + insert + "\n");
        }

        // The beat line's newline stays with the beat; the new block follows it with a blank line each side.
        var at = beat.End + 1;
        return new ProseDocument(Text.Insert(at, "\n" + insert + "\n"));
    }

    // Replaces everything under the beat's line (every block up to the next ::beat line, or the end of the body) with
    // `markdown`, one blank line apart from the beat and from the next beat. Embeds in the removed text go with it.
    public ProseDocument ReplaceBeatProse(Guid beatId, string markdown)
    {
        var insert = markdown.TrimEnd('\n');
        if (insert.Length == 0)
        {
            throw new ProseEditException(ProseEditFailure.EmptyInsert, "Nothing to insert.");
        }

        var index = Blocks.ToList().FindIndex(b => b.Directive == "beat" && b.Id == beatId);
        if (index < 0)
        {
            throw new ProseEditException(ProseEditFailure.TargetNotFound, $"No beat {beatId} in the body.");
        }

        var head = Text[..Blocks[index].End];
        var next = Blocks.Skip(index + 1).FirstOrDefault(b => b.Directive == "beat");
        var tail = next == null ? "" : "\n" + Text[next.Start..];
        return new ProseDocument(head + "\n\n" + insert + "\n" + tail);
    }

    // Removes the ::kb-addition line and one blank line beside it, so blocks stay one blank line apart.
    public ProseDocument StripKbAddition(Guid additionId) => StripDirective("kb-addition", additionId);

    // Removes the ::figure line the same way.
    public ProseDocument StripFigure(Guid figureId) => StripDirective("figure", figureId);

    private ProseDocument StripDirective(string directive, Guid id)
    {
        var index = Blocks.ToList().FindIndex(b => b.Directive == directive && b.Id == id);
        if (index < 0)
        {
            throw new ProseEditException(ProseEditFailure.TargetNotFound, $"No {directive} {id} in the body.");
        }

        var block = Blocks[index];
        var start = block.Start;
        var end = Math.Min(block.End + 1, Text.Length);
        if (end < Text.Length && Text[end] == '\n')
        {
            end++;
        }
        else if (start >= 2 && Text[start - 1] == '\n' && Text[start - 2] == '\n')
        {
            start--;
        }

        return new ProseDocument(Text.Remove(start, end - start));
    }

    // Turns every [Name](kb:entryId) into Name. Returns this document when there is none.
    public ProseDocument ReplaceMentionWithText(Guid entryId) =>
        Replace(l => l.Scheme == "kb" && l.Id == entryId, l => l.Text, out _);

    // Wraps the first occurrence of passageText as [passageText](ref:useId). The passage must sit inside one
    // paragraph-like block, outside code and outside any existing link, and hold no unescaped bracket.
    public ProseDocument WrapRef(string passageText, Guid useId) =>
        WrapFirst(passageText, "ref", useId, wholeWords: false)
        ?? throw new ProseEditException(ProseEditFailure.PassageNotWrappable, "The passage is not in the body in a place a reference can mark.");

    // Wraps Text[start..start+length] as a reference mark, under the same rules as the text overload.
    public ProseDocument WrapRef(int start, int length, Guid useId) =>
        start >= 0 && length > 0 && start + length <= Text.Length && TryWrap(start, length, "ref", useId, out var wrapped)
            ? wrapped
            : throw new ProseEditException(ProseEditFailure.PassageNotWrappable, "The range is not a place a reference can mark.");

    // Turns the first plain occurrence of `name` as a whole word into [name](kb:entryId) ("Make mention"), under the
    // WrapRef rules: inside one paragraph-like block, outside code and any existing link, no unescaped bracket.
    public ProseDocument WrapMention(string name, Guid entryId) =>
        WrapFirst(name, "kb", entryId, wholeWords: true)
        ?? throw new ProseEditException(ProseEditFailure.PassageNotWrappable, $"\"{name}\" has no plain occurrence that can become a mention.");

    private ProseDocument? WrapFirst(string text, string scheme, Guid id, bool wholeWords)
    {
        if (text.Length == 0)
        {
            return null;
        }

        foreach (var block in Blocks.Where(IsWrappable))
        {
            var at = Text.IndexOf(text, block.Start, block.End - block.Start, StringComparison.Ordinal);
            while (at >= 0)
            {
                if ((!wholeWords || IsWordBoundary(at - 1) && IsWordBoundary(at + text.Length))
                    && TryWrap(at, text.Length, scheme, id, out var wrapped))
                {
                    return wrapped;
                }

                var next = at + 1;
                at = next >= block.End ? -1 : Text.IndexOf(text, next, block.End - next, StringComparison.Ordinal);
            }
        }

        return null;
    }

    private bool IsWordBoundary(int index) => index < 0 || index >= Text.Length || !char.IsLetterOrDigit(Text[index]);

    // Turns every mark with this use id back into its text; a bare [^] marker is removed.
    public ProseDocument UnwrapRef(Guid useId)
    {
        var result = Replace(
            l => l.Scheme == "ref" && l.Id == useId,
            l => l.Text == "^" ? string.Empty : l.Text,
            out var count);
        return count == 0
            ? throw new ProseEditException(ProseEditFailure.TargetNotFound, $"No reference mark {useId} in the body.")
            : result;
    }

    private bool TryWrap(int start, int length, string scheme, Guid id, out ProseDocument wrapped)
    {
        wrapped = this;
        var end = start + length;
        var block = Blocks.FirstOrDefault(b => start >= b.Start && end <= b.End);
        if (block is null || !IsWrappable(block))
        {
            return false;
        }

        var passage = Text.Substring(start, length);
        if (UnescapedBracket().IsMatch(passage) || passage.Contains("\n\n", StringComparison.Ordinal) || passage.Trim() != passage)
        {
            return false;
        }

        if (block.Kind == ProseBlockKind.Table && !FitsInCell(block, start, passage))
        {
            return false;
        }

        if (InlineLinks(block).Any(l => l.Start < end && start < l.Start + l.Length) || CodeSpans(block).Any(c => block.Start + c.Index < end && start < block.Start + c.Index + c.Length))
        {
            return false;
        }

        wrapped = new ProseDocument(Text[..start] + "[" + passage + "](" + scheme + ":" + id.ToString("D") + ")" + Text[end..]);
        return true;
    }

    // A mark inside a table stays within one cell of a body or header row: no newline, no unescaped pipe, not the delimiter row.
    private bool FitsInCell(ProseBlock block, int start, string passage)
    {
        if (passage.Contains('\n') || UnescapedPipe().IsMatch(passage))
        {
            return false;
        }

        var delimiterStart = Text.IndexOf('\n', block.Start) + 1;
        var delimiterEnd = Text.IndexOf('\n', delimiterStart);
        return start < delimiterStart || start > (delimiterEnd < 0 ? Text.Length : delimiterEnd);
    }

    [GeneratedRegex("(?<!\\\\)\\|")]
    private static partial Regex UnescapedPipe();

    private ProseDocument Replace(Func<LinkSpan, bool> matches, Func<LinkSpan, string> replacement, out int count)
    {
        var builder = new StringBuilder();
        var position = 0;
        count = 0;
        foreach (var link in Blocks.Where(IsWrappable).SelectMany(InlineLinks).Where(matches))
        {
            builder.Append(Text, position, link.Start - position).Append(replacement(link));
            position = link.Start + link.Length;
            count++;
        }

        return count == 0 ? this : new ProseDocument(builder.Append(Text, position, Text.Length - position).ToString());
    }

    private static bool IsWrappable(ProseBlock block) =>
        block.Kind is ProseBlockKind.Paragraph or ProseBlockKind.Heading or ProseBlockKind.Quote or ProseBlockKind.List or ProseBlockKind.Table;

    private sealed record LinkSpan(int Start, int Length, string Scheme, Guid Id, string Text);

    // Spans carry offsets into the whole body. Code spans are masked first so link syntax inside them is ignored.
    private IEnumerable<LinkSpan> InlineLinks(ProseBlock block) =>
        InlineLink().Matches(MaskCode(block)).Select(m => new LinkSpan(
            block.Start + m.Index,
            m.Length,
            m.Groups["scheme"].Value,
            Guid.Parse(m.Groups["id"].Value),
            Text.Substring(block.Start + m.Groups["text"].Index, m.Groups["text"].Length)));

    private IEnumerable<Match> CodeSpans(ProseBlock block) =>
        CodeSpan().Matches(Text.Substring(block.Start, block.End - block.Start));

    // Code spans, and the opaque blocks (an HTML comment, a fence) that a quote or list item holds, are blanked out.
    private string MaskCode(ProseBlock block)
    {
        var masked = CodeSpan().Replace(Text.Substring(block.Start, block.End - block.Start), m => new string(' ', m.Length)).ToCharArray();
        foreach (var (start, end) in OpaqueSpans(block))
        {
            for (var i = start; i < end; i++)
            {
                if (masked[i - block.Start] != '\n')
                {
                    masked[i - block.Start] = ' ';
                }
            }
        }

        return new string(masked);
    }

    // The opaque and fence blocks inside a quote or a list, which the top-level pass does not look into: the same block
    // grammar over the quote's lines without their `>`, and over each item's lines without its marker and indent.
    private IEnumerable<(int Start, int End)> OpaqueSpans(ProseBlock block)
    {
        var spans = new List<(int, int)>();
        if (block.Kind is ProseBlockKind.Quote or ProseBlockKind.List)
        {
            CollectOpaque(block.Kind, SplitLines(Text, block.Start, block.End), spans);
        }

        return spans;
    }

    private static void CollectOpaque(ProseBlockKind kind, List<Line> lines, List<(int, int)> spans)
    {
        foreach (var body in kind == ProseBlockKind.Quote ? [QuoteBody(lines)] : ItemBodies(lines))
        {
            var blocks = new List<ProseBlock>();
            ParseRange(body, 0, body.Count, blocks, inItem: kind == ProseBlockKind.List);
            foreach (var inner in blocks)
            {
                if (inner.Kind is ProseBlockKind.Opaque or ProseBlockKind.Fence)
                {
                    spans.Add((inner.Start, inner.End));
                }
                else if (inner.Kind is ProseBlockKind.Quote or ProseBlockKind.List)
                {
                    CollectOpaque(inner.Kind, body.Where(l => l.Start >= inner.Start && l.End <= inner.End).ToList(), spans);
                }
            }
        }
    }

    private static List<Line> QuoteBody(List<Line> lines) =>
        lines.Select(l => Cut(l, l.Text.Length > 1 && l.Text[1] == ' ' ? 2 : 1)).ToList();

    // Each item's lines: the text after its marker, then the lines indented under it with that indent cut.
    private static List<List<Line>> ItemBodies(List<Line> lines)
    {
        var bodies = new List<List<Line>>();
        var width = 0;
        foreach (var line in lines)
        {
            var marker = ListMarkerWidth(line.Text);
            if (marker > 0 && !(bodies.Count > 0 && line.Text.StartsWith(new string(' ', width), StringComparison.Ordinal)))
            {
                width = marker;
                bodies.Add([Cut(line, marker)]);
            }
            else
            {
                bodies[^1].Add(Cut(line, width));
            }
        }

        return bodies;
    }

    private static Line Cut(Line line, int count)
    {
        var cut = Math.Min(count, line.Text.Length);
        return new Line(line.Start + cut, line.End, line.Text[cut..]);
    }

    private static ProseEmbedKind DirectiveKind(string name) => name switch
    {
        "beat" => ProseEmbedKind.Beat,
        "kb-addition" => ProseEmbedKind.KbAddition,
        "figure" => ProseEmbedKind.Figure,
        "spell" => ProseEmbedKind.Spell,
        _ => ProseEmbedKind.StatBlock,
    };

    private static ProseEmbedKind LinkKind(string scheme) => scheme switch
    {
        "kb" => ProseEmbedKind.Mention,
        "chapter" => ProseEmbedKind.ChapterLink,
        _ => ProseEmbedKind.Ref,
    };

    // The block grammar of docs/prose-format.md, the same one src/prose/markdown/parse.ts reads, at the top level and
    // inside known containers. Quotes and lists are not looked into: their lines are text.
    private static List<ProseBlock> ParseBlocks(string text)
    {
        var lines = SplitLines(text, 0, text.Length);
        var blocks = new List<ProseBlock>();
        ParseRange(lines, 0, lines.Count, blocks);
        return blocks;
    }

    private static List<Line> SplitLines(string text, int from, int to)
    {
        var lines = new List<Line>();
        for (var position = from; position < to;)
        {
            var newline = text.IndexOf('\n', position, to - position);
            var end = newline < 0 ? to : newline;
            lines.Add(new Line(position, end, text[position..end]));
            position = end + 1;
        }

        return lines;
    }

    // Start and End are offsets into the whole body; inside a quote or list item the Text is cut at the line's content.
    private sealed record Line(int Start, int End, string Text);

    // `inItem` is true for the body of a list item, where a paragraph also ends at a list marker line.
    private static void ParseRange(List<Line> lines, int from, int to, List<ProseBlock> blocks, bool inItem = false)
    {
        var i = from;
        while (i < to)
        {
            var line = lines[i].Text;
            if (line.Length == 0)
            {
                i++;
            }
            else if (line.StartsWith("```", StringComparison.Ordinal))
            {
                // Closed only by a line that is exactly ```; an unclosed fence runs to the last non-blank line.
                var close = IndexOfLine(lines, i + 1, to, "```");
                var last = close >= 0 ? close : LastNonBlank(lines, i, to);
                blocks.Add(new ProseBlock(ProseBlockKind.Fence, lines[i].Start, lines[last].End));
                i = last + 1;
            }
            else if (line.StartsWith("<!--", StringComparison.Ordinal))
            {
                // Through the line that holds `-->`; unclosed, to the last non-blank line.
                var close = CommentEnd(lines, i, to);
                var last = close >= 0 ? close : LastNonBlank(lines, i, to);
                blocks.Add(new ProseBlock(ProseBlockKind.Opaque, lines[i].Start, lines[last].End));
                i = last + 1;
            }
            else if (RuleLine().IsMatch(line))
            {
                blocks.Add(new ProseBlock(ProseBlockKind.Rule, lines[i].Start, lines[i].End));
                i++;
            }
            else if (line.StartsWith('|') && i + 1 < to && ProseTable.IsDelimiterRow(lines[i + 1].Text, ProseTable.SplitRow(line).Count))
            {
                var last = i + 1;
                while (last + 1 < to && lines[last + 1].Text.StartsWith('|'))
                {
                    last++;
                }

                blocks.Add(new ProseBlock(ProseBlockKind.Table, lines[i].Start, lines[last].End));
                i = last + 1;
            }
            else if (line.StartsWith(":::", StringComparison.Ordinal))
            {
                i = ParseContainer(lines, i, to, blocks);
            }
            else if (TryDirective(line, out var name, out var id))
            {
                blocks.Add(new ProseBlock(ProseBlockKind.Directive, lines[i].Start, lines[i].End, name, id));
                i++;
            }
            else if (line.StartsWith("::", StringComparison.Ordinal))
            {
                blocks.Add(new ProseBlock(ProseBlockKind.Opaque, lines[i].Start, lines[i].End));
                i++;
            }
            else
            {
                var kind = TextKind(line);
                var width = ListMarkerWidth(line);
                var last = i;
                while (last + 1 < to)
                {
                    if (kind == ProseBlockKind.List && lines[last + 1].Text.Length == 0)
                    {
                        // Blank lines stay in the item when the next line is still indented under it.
                        var ahead = last + 1;
                        while (ahead < to && lines[ahead].Text.Length == 0)
                        {
                            ahead++;
                        }

                        if (ahead < to && lines[ahead].Text.StartsWith(new string(' ', width), StringComparison.Ordinal))
                        {
                            last = ahead;
                            continue;
                        }

                        break;
                    }

                    if (!Continues(kind, lines[last + 1].Text, line, ref width, inItem))
                    {
                        break;
                    }

                    last++;
                }

                blocks.Add(new ProseBlock(kind, lines[i].Start, lines[last].End));
                i = last + 1;
            }
        }
    }

    // A known container whose bare ::: closes it gives two markers with ordinary blocks between; an unknown or
    // attribute-mismatched one is opaque through its :::, an unclosed one to the last non-blank line, a stray line alone.
    private static int ParseContainer(List<Line> lines, int i, int to, List<ProseBlock> blocks)
    {
        var header = ContainerLine().Match(lines[i].Text);
        var close = IndexOfLine(lines, i + 1, to, ":::");
        if (header.Success && close >= 0 && IsKnownContainer(header))
        {
            blocks.Add(new ProseBlock(ProseBlockKind.ContainerMarker, lines[i].Start, lines[i].End));
            ParseRange(lines, i + 1, close, blocks);
            blocks.Add(new ProseBlock(ProseBlockKind.ContainerMarker, lines[close].Start, lines[close].End));
            return close + 1;
        }

        var last = i;
        if (header.Success)
        {
            last = close >= 0 ? close : LastNonBlank(lines, i, to);
        }

        blocks.Add(new ProseBlock(ProseBlockKind.Opaque, lines[i].Start, lines[last].End));
        return last + 1;
    }

    private static bool IsKnownContainer(Match header)
    {
        var name = header.Groups["name"].Value;
        return name is "readaloud" or "sidebar" or "aside" && (!header.Groups["title"].Success || name == "sidebar");
    }

    [GeneratedRegex("^:::(?<name>[a-z][a-z0-9-]*)(?:\\{title=\"(?<title>[^\"]*)\"\\})?$")]
    private static partial Regex ContainerLine();

    // The line that holds `-->`, looking on the opening line after its `<!--`; -1 when there is none.
    private static int CommentEnd(List<Line> lines, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (lines[i].Text.IndexOf("-->", i == from ? 4 : 0, StringComparison.Ordinal) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfLine(List<Line> lines, int from, int to, string text)
    {
        for (var i = from; i < to; i++)
        {
            if (lines[i].Text == text)
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastNonBlank(List<Line> lines, int from, int to)
    {
        var last = to - 1;
        while (last > from && lines[last].Text.Length == 0)
        {
            last--;
        }

        return last;
    }

    // A paragraph runs to a blank, `::` or ``` line (in a list item, also to a list marker line); a heading is one line; a quote
    // is its run of `>` lines; a list runs while lines are items of the same kind or are indented under the current item,
    // blank lines included when an indented line follows them.
    private static bool Continues(ProseBlockKind kind, string next, string first, ref int width, bool inItem)
    {
        if (next.Length == 0 || next.StartsWith("::", StringComparison.Ordinal) || next.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        switch (kind)
        {
            case ProseBlockKind.Heading:
                return false;
            case ProseBlockKind.Quote:
                return next[0] == '>';
            case ProseBlockKind.List when OrderedItem().IsMatch(next) == OrderedItem().IsMatch(first) && ListMarkerWidth(next) > 0:
                width = ListMarkerWidth(next);
                return true;
            case ProseBlockKind.List:
                return next.StartsWith(new string(' ', width), StringComparison.Ordinal);
            default:
                return !(inItem && ListMarkerWidth(next) > 0);
        }
    }

    private static ProseBlockKind TextKind(string line)
    {
        if (HeadingLine().IsMatch(line))
        {
            return ProseBlockKind.Heading;
        }

        if (line[0] == '>')
        {
            return ProseBlockKind.Quote;
        }

        return ListMarkerWidth(line) > 0 ? ProseBlockKind.List : ProseBlockKind.Paragraph;
    }

    // Width of a list item's marker ("- " is 2, "12. " is 4; a bare "-" or "1." counts its missing space), or 0.
    private static int ListMarkerWidth(string line)
    {
        var match = BulletItem().Match(line);
        if (!match.Success)
        {
            match = OrderedItem().Match(line);
        }

        if (!match.Success)
        {
            return 0;
        }

        var text = match.Groups["text"];
        if (text.Success && text.Length > 0)
        {
            return text.Index;
        }

        return line.EndsWith(' ') ? line.Length : line.Length + 1;
    }

    [GeneratedRegex("^(?:---|\\*\\*\\*|___)$")]
    private static partial Regex RuleLine();

    [GeneratedRegex("^#{1,6} .+$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex("^-(?: (?<text>.*))?$")]
    private static partial Regex BulletItem();

    [GeneratedRegex("^\\d+\\.(?: (?<text>.*))?$")]
    private static partial Regex OrderedItem();

    private static bool TryDirective(string line, out string name, out Guid? id)
    {
        name = string.Empty;
        id = null;
        if (!line.StartsWith("::", StringComparison.Ordinal) || line.StartsWith(":::", StringComparison.Ordinal))
        {
            return false;
        }

        var m = IdDirective().Match(line);
        if (!m.Success)
        {
            m = KbDirective().Match(line);
        }

        if (m.Success)
        {
            name = m.Groups["name"].Value;
            id = Guid.Parse(m.Groups["id"].Value);
            return true;
        }

        if (PageBreak().IsMatch(line))
        {
            name = "pagebreak";
            return true;
        }

        return false;
    }
}
