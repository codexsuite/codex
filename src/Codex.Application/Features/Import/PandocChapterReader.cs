using System.Text.RegularExpressions;

namespace Codex.Application.Features.Import;

// Reads one chapter file of a pandoc book folder (Markdown with raw LaTeX, as `pandoc -f markdown+raw_tex` reads it,
// written for the rpgtex dndbook class) into PandocBlocks, mapping the LaTeX of issue #13's import mapping:
//   figure environment with \part{X}     Part X, its \includegraphics the opener art
//   other figure with one \includegraphics   Figure (figure[p] page, figure* float-top, figure column), \caption
//   ![cap](path){…}                        Figure, column (the path URL-decoded; attributes dropped)
//   \DndDropCapLine{F}ighters              Fighters (the template draws drop caps)
//   \onecolumn, \twocolumn                 dropped
//   \newpage, \clearpage                   Break; Read drops the ones around a part opener and at the file's ends
//   DndReadAloud / DndSidebar / DndComment Container readaloud / sidebar (title) / aside
//   \DndSpellHeader{6 args}                Spell; the text after it, to the next header, heading or the end of its
//                                          fence, is the spell's own text (Children)
//   ```{=latex} holding a spell header     read like the rest of the file; other {=latex} fences stay Latex
//   # 3.1 Fighter                          Chapter "Fighter"; deeper headings move up one level, unnumbered
//   pipe table                             Table, rows as written      <!-- … -->   Comment
//   any other LaTeX                        Latex: the whole paragraph or environment, never left as text
// Pure.
public sealed partial class PandocChapterReader
{
    public static readonly IReadOnlyList<string> SpellFields = ["level-school", "casting-time", "range", "components", "duration"];

    private static readonly string[] Containers = ["DndReadAloud", "DndSidebar", "DndComment"];

    private readonly List<string> _lines;
    private readonly bool _nested;
    private int _i;

    private PandocChapterReader(IEnumerable<string> lines, bool nested)
    {
        _lines = lines.ToList();
        _nested = nested;
    }

    public static IReadOnlyList<PandocBlock> Read(string text)
    {
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return WithoutWrappingBreaks(new PandocChapterReader(lines, false).ReadBlocks());
    }

    // "3.1 Fighter" -> "Fighter", "3.1.1. Overview" -> "Overview", "2. Races" -> "Races"; "1984" stays.
    public static string StripNumber(string heading)
    {
        var text = HeadingAttributes().Replace(heading.Trim(), "");
        var stripped = HandNumber().Replace(text, "");
        return stripped.Length == 0 ? text : stripped;
    }

    private List<PandocBlock> ReadBlocks()
    {
        var output = new List<PandocBlock>();
        var run = new List<string>();
        while (_i < _lines.Count)
        {
            var line = _lines[_i];
            var trimmed = line.Trim();
            var comment = line.IndexOf("<!--", StringComparison.Ordinal);
            if (comment > 0 && line[..comment].Trim().Length > 0)
            {
                // Text, then a comment: the text stays in its paragraph and the comment is read on its own line.
                _lines[_i] = line[..comment].TrimEnd();
                _lines.Insert(_i + 1, line[comment..]);
                run.Add(_lines[_i++]);
            }
            else if (trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                Flush(run, output);
                output.Add(new PandocBlock(PandocBlockKind.Comment, Text: ReadComment()));
            }
            else if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Flush(run, output);
                ReadFence(output);
            }
            else if (BeginLine().Match(trimmed) is { Success: true } begin)
            {
                Flush(run, output);
                ReadEnvironment(begin.Groups["name"].Value, output);
            }
            else if (ColumnLine().IsMatch(trimmed))
            {
                _i++;
            }
            else if (BreakLine().IsMatch(trimmed))
            {
                Flush(run, output);
                output.Add(new PandocBlock(PandocBlockKind.Break));
                _i++;
            }
            else if (trimmed.StartsWith("\\DndSpellHeader", StringComparison.Ordinal) && ReadSpell() is { } spell)
            {
                Flush(run, output);
                output.Add(spell);
            }
            else if (!_nested && PartLine().Match(trimmed) is { Success: true } part && Group(trimmed, part.Length - 1) is { } partTitle
                     && trimmed[(partTitle.End + 1)..].Trim().Length == 0)
            {
                Flush(run, output);
                output.Add(new PandocBlock(PandocBlockKind.Part, Title: Plain(partTitle.Text)));
                _i++;
            }
            else if (!_nested && ChapterLine().Match(line) is { Success: true } chapter)
            {
                Flush(run, output);
                output.Add(new PandocBlock(PandocBlockKind.Chapter, Title: StripNumber(Plain(chapter.Groups[1].Value))));
                _i++;
            }
            else if (ImageLine().Match(trimmed) is { Success: true } image)
            {
                Flush(run, output);
                output.Add(Figure(image, "column"));
                _i++;
            }
            else if (trimmed.Contains('|') && _i + 1 < _lines.Count && _lines[_i + 1].Contains('|') && DelimiterRow().IsMatch(_lines[_i + 1].Trim()))
            {
                Flush(run, output);
                var rows = new List<string>();
                while (_i < _lines.Count && _lines[_i].Trim().Length > 0 && _lines[_i].Contains('|'))
                {
                    rows.Add(_lines[_i++].Trim());
                }
                output.Add(new PandocBlock(PandocBlockKind.Table, Text: string.Join('\n', rows)));
            }
            else
            {
                run.Add(line);
                _i++;
            }
        }
        Flush(run, output);
        return output;
    }

    private string ReadComment()
    {
        var lines = new List<string>();
        var first = true;
        while (_i < _lines.Count)
        {
            var line = first ? _lines[_i].TrimStart() : _lines[_i];
            var end = line.IndexOf("-->", first ? 4 : 0, StringComparison.Ordinal);
            first = false;
            if (end < 0)
            {
                lines.Add(line);
                _i++;
                continue;
            }
            lines.Add(line[..(end + 3)]);
            var rest = line[(end + 3)..];
            if (rest.Trim().Length > 0)
            {
                _lines[_i] = rest;
            }
            else
            {
                _i++;
            }
            break;
        }
        return string.Join('\n', lines);
    }

    private void ReadFence(List<PandocBlock> output)
    {
        var open = _lines[_i++].Trim();
        var info = open[3..].Trim();
        var body = new List<string>();
        while (_i < _lines.Count && _lines[_i].Trim() != "```")
        {
            body.Add(_lines[_i++]);
        }
        var closed = _i < _lines.Count;
        if (closed)
        {
            _i++;
        }
        if (info is "{=latex}" or "{=tex}")
        {
            if (body.Any(l => l.Contains("\\DndSpellHeader", StringComparison.Ordinal)))
            {
                output.AddRange(new PandocChapterReader(body, _nested).ReadBlocks());
            }
            else
            {
                output.Add(new PandocBlock(PandocBlockKind.Latex, Text: string.Join('\n', body)));
            }
            return;
        }
        output.Add(new PandocBlock(PandocBlockKind.Text, Text: string.Join('\n', body.Prepend(open).Append("```"))));
    }

    // \begin{name} … the matching \end{name}, nesting counted; text after \end{name} on its line is read on.
    private void ReadEnvironment(string name, List<PandocBlock> output)
    {
        var begin = "\\begin{" + name + "}";
        var end = "\\end{" + name + "}";
        var lines = new List<string>();
        var depth = 0;
        while (_i < _lines.Count)
        {
            var line = _lines[_i];
            depth += Count(line, begin) - Count(line, end);
            if (depth > 0 || _i + 1 == _lines.Count)
            {
                lines.Add(line);
                _i++;
                continue;
            }
            var after = line.LastIndexOf(end, StringComparison.Ordinal);
            var cut = after < 0 ? line.Length : after + end.Length;
            lines.Add(line[..cut]);
            if (line[cut..].Trim().Length > 0)
            {
                _lines[_i] = line[cut..];
            }
            else
            {
                _i++;
            }
            break;
        }

        var raw = string.Join('\n', lines).Trim();
        if (name is "figure" or "figure*")
        {
            output.Add(FigureEnvironment(name, raw));
        }
        else if (Containers.Contains(name) && !_nested)
        {
            output.Add(Container(name, raw, begin, end));
        }
        else
        {
            output.Add(new PandocBlock(PandocBlockKind.Latex, Text: raw));
        }
    }

    private static PandocBlock FigureEnvironment(string name, string raw)
    {
        var options = FigureOptions().Match(raw).Groups[1].Value;
        var graphics = IncludeGraphics().Matches(raw);
        var caption = Command(raw, CaptionCommand());
        if (Command(raw, PartCommand()) is { } title)
        {
            return new PandocBlock(PandocBlockKind.Part, Title: Plain(title), Image: graphics.Count > 0 ? graphics[0].Groups[1].Value.Trim() : null,
                Caption: caption == null ? null : Plain(caption));
        }
        if (graphics.Count != 1)
        {
            return new PandocBlock(PandocBlockKind.Latex, Text: raw);
        }
        var placement = "column";
        if (options.Contains('p'))
        {
            placement = "page";
        }
        else if (name == "figure*")
        {
            placement = "float-top";
        }
        return new PandocBlock(PandocBlockKind.Figure, Image: graphics[0].Groups[1].Value.Trim(), Placement: placement,
            Caption: caption == null ? null : Plain(caption));
    }

    private static PandocBlock Container(string name, string raw, string begin, string end)
    {
        var at = begin.Length;
        if (raw.Length > at && raw[at] == '[' && raw.IndexOf(']', at) is var close and > 0)
        {
            at = close + 1;
        }
        string? title = null;
        if (name != "DndReadAloud" && raw.Length > at && raw[at] == '{' && Group(raw, at) is { } group)
        {
            title = Plain(group.Text);
            at = group.End + 1;
        }
        var stop = raw.LastIndexOf(end, StringComparison.Ordinal);
        var inner = raw[at..(stop < at ? raw.Length : stop)];
        var children = new PandocChapterReader(inner.Split('\n'), true).ReadBlocks();
        if (name == "DndComment" && title is { Length: > 0 })
        {
            // :::aside has no title in the prose format (docs/prose-format.md): the title opens the box in bold.
            children = children.Prepend(new PandocBlock(PandocBlockKind.Text, Text: "**" + title + "**")).ToList();
            title = null;
        }
        return new PandocBlock(PandocBlockKind.Container, Title: title, Container: name switch
        {
            "DndReadAloud" => "readaloud",
            "DndSidebar" => "sidebar",
            _ => "aside",
        }, Children: children);
    }

    // \DndSpellHeader and its six {…} arguments, possibly over several lines; null when they are not all there.
    private PandocBlock? ReadSpell()
    {
        var window = string.Join('\n', _lines.Skip(_i).Take(40));
        var at = window.IndexOf("\\DndSpellHeader", StringComparison.Ordinal) + "\\DndSpellHeader".Length;
        var args = new List<string>();
        while (args.Count < 6)
        {
            while (at < window.Length && char.IsWhiteSpace(window[at]))
            {
                at++;
            }
            if (at >= window.Length || window[at] != '{' || Group(window, at) is not { } arg)
            {
                return null;
            }
            args.Add(Plain(arg.Text));
            at = arg.End + 1;
        }

        var consumed = window[..at];
        _i += consumed.Count(c => c == '\n');
        var rest = _lines[_i][(consumed.Length - consumed.LastIndexOf('\n') - 1)..];
        if (rest.Trim().Length > 0)
        {
            _lines[_i] = rest;
        }
        else
        {
            _i++;
        }

        var body = new List<string>();
        while (_i < _lines.Count && !EndsSpellText(_lines[_i]))
        {
            body.Add(_lines[_i++]);
        }
        var fields = SpellFields.Select((key, n) => (key, args[n + 1])).ToDictionary(f => f.key, f => f.Item2, StringComparer.Ordinal);
        return new PandocBlock(PandocBlockKind.Spell, Title: args[0], Text: consumed.Trim(), Fields: fields,
            Children: new PandocChapterReader(body, true).ReadBlocks());
    }

    private static bool EndsSpellText(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("\\DndSpellHeader", StringComparison.Ordinal) || trimmed.StartsWith('#') || trimmed.StartsWith("```", StringComparison.Ordinal)
            || trimmed.StartsWith("<!--", StringComparison.Ordinal) || BeginLine().IsMatch(trimmed) || PartLine().IsMatch(trimmed)
            || BreakLine().IsMatch(trimmed) || ImageLine().IsMatch(trimmed);
    }

    // A run of plain lines, paragraph by paragraph: LaTeX nothing maps makes its paragraph a Latex block, an image
    // inside a line becomes a Figure after its paragraph, and the rest joins one Text block.
    private void Flush(List<string> run, List<PandocBlock> output)
    {
        var text = new List<string>();
        foreach (var paragraph in Paragraphs(run))
        {
            var figures = new List<PandocBlock>();
            var lines = paragraph.Select(line =>
            {
                var mapped = DropCap().Replace(line, m => m.Groups[1].Value + m.Groups[2].Value);
                if (HeadingLine().Match(mapped) is { Success: true } heading)
                {
                    var level = heading.Groups[1].Length;
                    mapped = new string('#', _nested || level == 1 ? level : level - 1) + " " + StripNumber(heading.Groups[2].Value);
                }
                return InlineImage().Replace(mapped, m =>
                {
                    figures.Add(Figure(m, "column"));
                    return "";
                }).TrimEnd();
            }).Where(l => l.Trim().Length > 0).ToList();
            var joined = string.Join('\n', lines);
            if (LatexCommand().IsMatch(joined))
            {
                AddText(text, output);
                output.Add(new PandocBlock(PandocBlockKind.Latex, Text: joined));
            }
            else if (joined.Length > 0)
            {
                text.Add(joined);
            }
            if (figures.Count > 0)
            {
                AddText(text, output);
                output.AddRange(figures);
            }
        }
        AddText(text, output);
        run.Clear();
    }

    private static void AddText(List<string> text, List<PandocBlock> output)
    {
        if (text.Count > 0)
        {
            output.Add(new PandocBlock(PandocBlockKind.Text, Text: string.Join("\n\n", text)));
            text.Clear();
        }
    }

    private static IEnumerable<List<string>> Paragraphs(List<string> run)
    {
        var current = new List<string>();
        foreach (var line in run)
        {
            if (line.Trim().Length == 0)
            {
                if (current.Count > 0)
                {
                    yield return current;
                    current = [];
                }
                continue;
            }
            current.Add(line);
        }
        if (current.Count > 0)
        {
            yield return current;
        }
    }

    private static PandocBlock Figure(Match image, string placement)
    {
        var path = image.Groups["src"].Value;
        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            // Not percent-encoded after all: the path as written.
        }
        var caption = image.Groups["cap"].Value.Trim();
        return new PandocBlock(PandocBlockKind.Figure, Image: path, Placement: placement, Caption: caption.Length == 0 ? null : caption);
    }

    // Breaks next to a part opener (the \clearpage \onecolumn … \twocolumn \newpage around it), at either end of the
    // file, or straight after another break are not manual breaks.
    private static List<PandocBlock> WithoutWrappingBreaks(List<PandocBlock> blocks)
    {
        var drop = new bool[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Kind != PandocBlockKind.Part)
            {
                continue;
            }
            for (var j = i - 1; j >= 0 && blocks[j].Kind == PandocBlockKind.Break; j--)
            {
                drop[j] = true;
            }
            for (var j = i + 1; j < blocks.Count && blocks[j].Kind == PandocBlockKind.Break; j++)
            {
                drop[j] = true;
            }
        }
        var kept = blocks.Where((_, i) => !drop[i]).ToList();
        var output = new List<PandocBlock>();
        foreach (var block in kept)
        {
            var isBreak = block.Kind == PandocBlockKind.Break;
            if (isBreak && (output.Count == 0 || output[^1].Kind == PandocBlockKind.Break))
            {
                continue;
            }
            output.Add(block);
        }
        while (output.Count > 0 && output[^1].Kind == PandocBlockKind.Break)
        {
            output.RemoveAt(output.Count - 1);
        }
        return output;
    }

    // The argument of the first \command{…} in text (balanced braces), or null.
    private static string? Command(string text, Regex command)
    {
        var match = command.Match(text);
        return match.Success && Group(text, match.Index + match.Length - 1) is { } group ? group.Text : null;
    }

    // The balanced {…} group opening at text[open]: its inside and the index of its closing brace.
    private static (string Text, int End)? Group(string text, int open)
    {
        if (open >= text.Length || text[open] != '{')
        {
            return null;
        }
        var depth = 0;
        var i = open;
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }
            depth += text[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return (text[(open + 1)..i], i);
            }
            i++;
        }
        return null;
    }

    // LaTeX text as plain text: font commands lose their wrapper, escaped characters their backslash.
    private static string Plain(string latex)
    {
        var text = FontCommand().Replace(latex, "$1");
        text = EscapedCharacter().Replace(text, "$1").Replace('~', ' ');
        return Whitespace().Replace(text, " ").Trim();
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    [GeneratedRegex(@"^\\begin\{(?<name>[A-Za-z]+\*?)\}")]
    private static partial Regex BeginLine();

    [GeneratedRegex(@"^\\(?:onecolumn|twocolumn)$")]
    private static partial Regex ColumnLine();

    [GeneratedRegex(@"^\\(?:newpage|clearpage|pagebreak)$")]
    private static partial Regex BreakLine();

    [GeneratedRegex(@"^\\part\*?(?:\[[^\]]*\])?\{")]
    private static partial Regex PartLine();

    [GeneratedRegex(@"\\part\*?(?:\[[^\]]*\])?\{")]
    private static partial Regex PartCommand();

    [GeneratedRegex(@"\\caption(?:\[[^\]]*\])?\{")]
    private static partial Regex CaptionCommand();

    [GeneratedRegex(@"\\includegraphics(?:\[[^\]]*\])?\{([^}]*)\}")]
    private static partial Regex IncludeGraphics();

    [GeneratedRegex(@"^\\begin\{figure\*?\}\[([^\]]*)\]")]
    private static partial Regex FigureOptions();

    [GeneratedRegex(@"^# +(\S.*)$")]
    private static partial Regex ChapterLine();

    [GeneratedRegex(@"^ {0,3}(#{1,6}) +(\S.*)$")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^(?:\d+(?:\.\d+)+\.?|\d+\.)\s+(?=\S)")]
    private static partial Regex HandNumber();

    [GeneratedRegex(@"\s*\{[#.][^}]*\}\s*$")]
    private static partial Regex HeadingAttributes();

    [GeneratedRegex(@"^!\[(?<cap>[^\]]*)\]\((?<src>[^)\s]+)(?:\s+""[^""]*"")?\)(?:\{[^}]*\})?$")]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"!\[(?<cap>[^\]]*)\]\((?<src>[^)\s]+)(?:\s+""[^""]*"")?\)(?:\{[^}]*\})?")]
    private static partial Regex InlineImage();

    [GeneratedRegex(@"^\|?\s*:?-+:?\s*(?:\|\s*:?-+:?\s*)*\|?$")]
    private static partial Regex DelimiterRow();

    [GeneratedRegex(@"\\DndDropCapLine\{([^{}]*)\}(?:\{([^{}]*)\})?")]
    private static partial Regex DropCap();

    [GeneratedRegex(@"\\[A-Za-z@]+")]
    private static partial Regex LatexCommand();

    [GeneratedRegex(@"\\(?:textbf|textit|emph|textsc|textrm|text)\{([^{}]*)\}")]
    private static partial Regex FontCommand();

    [GeneratedRegex(@"\\([&%$#_{}])")]
    private static partial Regex EscapedCharacter();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
