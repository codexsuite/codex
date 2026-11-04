using System.Text;
using System.Text.RegularExpressions;
using Codex.Application.Prose;

namespace Codex.Application.Features.Import;

// One run of LatexImportMapper: walks the lines once, keeping HTML comments and pipe tables verbatim, mapping the
// known environments and commands, fencing the rest, and hands every other run of lines to ImportProse.ToStored.
// Inline LaTeX in those lines travels through ToStored as a private-use placeholder, so ToStored never escapes it.
internal sealed partial class LatexImportRun(Func<string, string?>? spellEntryId)
{
    private const char SpanOpen = '\uE000';
    private const char SpanClose = '\uE001';
    private const string PageBreak = "::pagebreak";

    private readonly Dictionary<string, int> _mapped = [];
    private readonly List<LatexImportFlag> _fenced = [];
    private readonly List<string> _spans = [];
    private readonly Dictionary<string, int> _numbered = [];

    public LatexImportResult Run(string markdown)
    {
        var text = markdown.TrimStart('\uFEFF').Replace("\0", "").Replace("\r\n", "\n").Replace('\r', '\n');
        var all = text.Split('\n');
        CountNumberedHeadings(all);
        var blocks = Blocks(all, 0, false);
        return new LatexImportResult(blocks.Count == 0 ? "" : string.Join("\n\n", blocks) + "\n", _mapped, _fenced);
    }

    private List<string> Blocks(string[] lines, int lineBase, bool inContainer)
    {
        var blocks = new List<string>();
        var text = new List<string>();
        var inFence = false;

        void Flush()
        {
            if (text.Count == 0)
            {
                return;
            }
            var stored = SpanPlaceholder().Replace(ImportProse.ToStored(string.Join('\n', text)), m => _spans[int.Parse(m.Groups[1].Value)]);
            text.Clear();
            if (stored.Length > 0)
            {
                blocks.Add(stored.TrimEnd('\n'));
            }
        }

        void Verbatim(string block)
        {
            Flush();
            blocks.Add(block);
        }

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (inFence)
            {
                text.Add(line);
                inFence = line.TrimEnd(' ', '\t') != "```";
                i++;
            }
            else if (line.TrimStart(' ').StartsWith("```", StringComparison.Ordinal) && line.Length - line.TrimStart(' ').Length <= 3)
            {
                text.Add(line);
                inFence = true;
                i++;
            }
            else if (line.StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = i;
                while (end < lines.Length - 1 && !(end == i ? line[4..] : lines[end]).Contains("-->", StringComparison.Ordinal))
                {
                    end++;
                }
                if (!(end == i ? line[4..] : lines[end]).Contains("-->", StringComparison.Ordinal))
                {
                    // Unclosed: the comment runs to the last non-blank line, not into the file's trailing newline.
                    while (end > i && lines[end].Trim().Length == 0)
                    {
                        end--;
                    }
                }
                Verbatim(string.Join('\n', lines[i..(end + 1)]));
                i = end + 1;
            }
            else if (line.StartsWith('|') && i + 1 < lines.Length && ProseTable.IsDelimiterRow(lines[i + 1], ProseTable.SplitRow(line).Count))
            {
                // A real table passes through; LaTeX in its cells becomes {=latex} spans, the delimiter row stays as it is.
                var end = i + 2;
                while (end < lines.Length && lines[end].StartsWith('|'))
                {
                    end++;
                }
                Verbatim(string.Join('\n', lines[i..end].Select((row, index) => index == 1 ? row : TableRow(row, lineBase + i + index + 1))));
                i = end;
            }
            else if (trimmed is "\\newpage" or "\\clearpage")
            {
                Count(LatexImportRule.PageBreak);
                Flush();
                if (blocks.Count == 0 || blocks[^1] != PageBreak)
                {
                    blocks.Add(PageBreak);
                }
                i++;
            }
            else if (trimmed is "\\onecolumn" or "\\twocolumn")
            {
                Count(LatexImportRule.ColumnDropped);
                i++;
            }
            else if (!inContainer && Container().Match(line) is { Success: true } env && EndOf(lines, i + 1, env.Groups[1].Value) is var close and >= 0)
            {
                Verbatim(MapContainer(env.Groups[1].Value, env.Groups[2].Value, lines[(i + 1)..close], lineBase + i + 1));
                i = close + 1;
            }
            else if (Figure().Match(line) is { Success: true } figure && EndOf(lines, i + 1, figure.Groups[1].Value) is var figureEnd and >= 0)
            {
                var body = lines[i..(figureEnd + 1)];
                var opener = body.Any(l => l.Contains("\\part", StringComparison.Ordinal));
                Count(LatexImportRule.FigureFenced);
                _fenced.Add(new LatexImportFlag(lineBase + i + 1, "block", opener ? "part+figure" : "figure", Excerpt(string.Join(' ', body.Skip(1).Select(l => l.Trim())))));
                Verbatim(Fence(body));
                i = figureEnd + 1;
            }
            else if (trimmed.StartsWith("\\DndSpellHeader", StringComparison.Ordinal) && SpellDirective(lines, i) is { Directive: var spell, Next: var next, Remainder: var rest })
            {
                Count(LatexImportRule.Spell);
                Verbatim(spell);
                if (rest.Length > 0)
                {
                    text.Add(rest);
                }
                i = next;
            }
            else
            {
                var mapped = DropCap(line);
                if (mapped.Length > 0 && mapped.TrimStart().StartsWith('\\') && char.IsAsciiLetter(mapped.TrimStart().ElementAtOrDefault(1)) && IsLatexAt(mapped.Trim(), 0) && IsBlockCommand(mapped.Trim()))
                {
                    var end = i;
                    var depth = 0;
                    var run = new List<string>();
                    while (end < lines.Length && lines[end].Trim().Length > 0 && (run.Count == 0 || depth > 0 || ContinuesCommand(lines[end].Trim())))
                    {
                        run.Add(end == i ? mapped : lines[end]);
                        depth = Math.Max(0, depth + BraceDepth(lines[end]));
                        end++;
                    }
                    Count(LatexImportRule.BlockFenced);
                    _fenced.Add(new LatexImportFlag(lineBase + i + 1, "block", CommandName(mapped.Trim()), Excerpt(string.Join(' ', run.Select(l => l.Trim())))));
                    Verbatim(Fence(run));
                    i = end;
                }
                else
                {
                    var joined = JoinOpenCommand(lines, i, mapped);
                    text.Add(Inline(StripNumber(joined.Line), lineBase + i + 1));
                    i += joined.Lines;
                }
            }
        }
        Flush();
        return blocks;
    }

    private string MapContainer(string name, string rest, string[] body, int lineBase)
    {
        var inner = Blocks(body, lineBase, true);
        var title = FirstBrace(rest);
        string open;
        switch (name)
        {
            case "DndReadAloud":
                Count(LatexImportRule.ReadAloud);
                open = ":::readaloud";
                break;
            case "DndSidebar":
                Count(LatexImportRule.Sidebar);
                open = title.Length > 0 ? $":::sidebar{{title=\"{title.Replace('"', '\'')}\"}}" : ":::sidebar";
                break;
            default:
                Count(LatexImportRule.Aside);
                open = ":::aside";
                if (title.Length > 0)
                {
                    inner.Insert(0, ImportProse.ToStored($"**{title}**").TrimEnd('\n'));
                }
                break;
        }
        return open + "\n" + (inner.Count > 0 ? string.Join("\n\n", inner) + "\n" : "") + ":::";
    }

    // The \DndSpellHeader at lines[start] as ::spell{kb}, when the caller knows the spell; the next line to read and
    // any text that followed the header's last argument on its line.
    private (string Directive, int Next, string Remainder)? SpellDirective(string[] lines, int start)
    {
        if (spellEntryId == null)
        {
            return null;
        }
        var end = start;
        while (end < lines.Length && lines[end].Trim().Length > 0)
        {
            end++;
        }
        var chunk = string.Join('\n', lines[start..end]);
        var from = chunk.IndexOf('\\', StringComparison.Ordinal);
        var stop = LatexArgs.CommandEnd(chunk, from, true);
        var name = LatexArgs.Groups(chunk, from + "\\DndSpellHeader".Length, stop).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(name) || spellEntryId(name) is not { } id || !Guid.TryParse(id, out var guid))
        {
            return null;
        }
        var consumed = chunk[..stop].Count(c => c == '\n');
        var rest = chunk[stop..].Split('\n')[0].Trim();
        return ($"::spell{{kb=\"{guid:D}\"}}", start + consumed + 1, rest);
    }

    // The line that closes the environment opened just before `from`, skipping inner ones of the same name.
    private static int EndOf(string[] lines, int from, string name)
    {
        var depth = 0;
        for (var j = from; j < lines.Length; j++)
        {
            var trimmed = lines[j].Trim();
            if (trimmed == $"\\end{{{name}}}" && depth-- == 0)
            {
                return j;
            }
            if (trimmed.StartsWith($"\\begin{{{name}}}", StringComparison.Ordinal))
            {
                depth++;
            }
        }
        return -1;
    }

    private string DropCap(string line)
    {
        return DropCapCommand().Replace(line, m =>
        {
            Count(LatexImportRule.DropCap);
            return m.Groups[1].Value;
        });
    }

    // Hand-numbered headings ("3.1.1 Title", "4.2 Title") lose their number, since the book numbers its own chapters.
    // Only multi-level numbers count, so "1984. The year" and "3. Alone" stay titles. A three-level number is always
    // numbering; a two-level one ("2.5 Million Reasons") only when another heading in the file starts with the same
    // number ("2.1 Introduction"), i.e. the file numbers its headings that way.
    private string StripNumber(string line)
    {
        var heading = HeadingNumber().Match(line);
        if (!heading.Success)
        {
            return line;
        }
        var number = heading.Groups[2].Value;
        if (number.Count(c => c == '.') < 2 && _numbered.GetValueOrDefault(number[..number.IndexOf('.', StringComparison.Ordinal)]) < 2)
        {
            return line;
        }
        Count(LatexImportRule.HeadingNumber);
        return heading.Groups[1].Value + " " + heading.Groups[3].Value;
    }

    // How many headings carry a multi-level number, per first number, outside code fences.
    private void CountNumberedHeadings(string[] lines)
    {
        var inFence = false;
        foreach (var line in lines)
        {
            if (line.TrimStart(' ').StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }
            else if (!inFence && HeadingNumber().Match(line) is { Success: true } heading)
            {
                var first = heading.Groups[2].Value[..heading.Groups[2].Value.IndexOf('.', StringComparison.Ordinal)];
                _numbered[first] = _numbered.GetValueOrDefault(first) + 1;
            }
        }
    }

    // A table row with its LaTeX commands as {=latex} spans; a literal pipe inside a span is escaped as the format requires.
    private string TableRow(string row, int lineNo) =>
        SpanPlaceholder().Replace(Inline(row, lineNo, escapePipes: true), m => _spans[int.Parse(m.Groups[1].Value)]);

    // A command whose argument runs on to the next lines is read as one line, so its braces balance; when they never
    // balance within the paragraph the first line stands alone. Returns the text and how many lines it took.
    private static (string Line, int Lines) JoinOpenCommand(string[] lines, int at, string first)
    {
        var joined = new StringBuilder(first);
        for (var next = at + 1; HasOpenArgument(joined.ToString()) && next < lines.Length && lines[next].Trim().Length > 0; next++)
        {
            joined.Append('\n').Append(lines[next]);
            if (!HasOpenArgument(joined.ToString()))
            {
                return (joined.ToString(), next - at + 1);
            }
        }
        return (first, 1);
    }

    private static bool HasOpenArgument(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '`')
            {
                var run = 1;
                while (i + run < text.Length && text[i + run] == '`')
                {
                    run++;
                }
                var close = text.IndexOf(new string('`', run), i + run, StringComparison.Ordinal);
                i = close < 0 ? i + run : close + run;
            }
            else if (text[i] == '\\' && i + 1 < text.Length && char.IsAsciiLetter(text[i + 1]))
            {
                var name = CommandName(text[i..]);
                var after = i + 1 + name.Length;
                if (IsCommand(name, after < text.Length ? text[after] : '\0') && after < text.Length && text[after] is '{' or '[' && LatexArgs.GroupEnd(text, after) < 0)
                {
                    return true;
                }
                i = after;
            }
            else
            {
                i += text[i] == '\\' ? 2 : 1;
            }
        }
        return false;
    }

    // `\name` is LaTeX when the mapper or a common text command knows it, or an argument follows it; anything else
    // (a Windows path, "\o/") is text, escaped by ImportProse as before.
    private static bool IsCommand(string name, char next) =>
        next is '{' or '[' or '*' || name.StartsWith("Dnd", StringComparison.Ordinal) || KnownCommands.Contains(name);

    private static readonly HashSet<string> KnownCommands = new(StringComparer.Ordinal)
    {
        "newpage", "clearpage", "onecolumn", "twocolumn", "part", "begin", "end", "item", "textbf", "textit", "textsc",
        "texttt", "textsf", "textrm", "emph", "underline", "vspace", "hspace", "vfill", "hfill", "smallskip", "medskip",
        "bigskip", "centering", "raggedright", "raggedleft", "noindent", "indent", "par", "newline", "linebreak",
        "pagebreak", "break", "includegraphics", "caption", "label", "ref", "pageref", "cite", "footnote", "ldots",
        "dots", "quad", "qquad", "bigskip", "tableofcontents", "maketitle", "section", "subsection", "chapter",
    };

    // Every \command with its arguments outside a code span becomes a placeholder for a {=latex} span.
    private string Inline(string line, int lineNo, bool escapePipes = false)
    {
        var result = new StringBuilder();
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (c == '`')
            {
                var run = 1;
                while (i + run < line.Length && line[i + run] == '`')
                {
                    run++;
                }
                var close = line.IndexOf(new string('`', run), i + run, StringComparison.Ordinal);
                var length = close < 0 ? run : close + run - i;
                result.Append(line, i, length);
                i += length;
            }
            else if (c == '\\' && i + 1 < line.Length && line[i + 1] == '\\')
            {
                result.Append("\\\\");
                i += 2;
            }
            else if (c == '\\' && i + 1 < line.Length && char.IsAsciiLetter(line[i + 1]) && IsLatexAt(line, i))
            {
                var end = LatexArgs.CommandEnd(line, i);
                var source = line[i..end].Replace('\n', ' ');
                Count(LatexImportRule.SpanFenced);
                _fenced.Add(new LatexImportFlag(lineNo, "span", CommandName(source), Excerpt(source)));
                _spans.Add(Span(escapePipes ? source.Replace("|", "\\|", StringComparison.Ordinal) : source));
                result.Append(SpanOpen).Append(_spans.Count - 1).Append(SpanClose);
                i = end;
            }
            else
            {
                result.Append(c);
                i++;
            }
        }
        return result.ToString();
    }

    private static bool IsLatexAt(string line, int at)
    {
        var name = CommandName(line[at..]);
        var after = at + 1 + name.Length;
        return IsCommand(name, after < line.Length ? line[after] : '\0');
    }

    private static string Span(string source)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in source)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', longest + 1);
        return $"{fence}{source}{fence}{{=latex}}";
    }

    // A line that starts with a command that is the whole line (its arguments may run on): a block, not a span.
    private static bool IsBlockCommand(string trimmed)
    {
        var rest = trimmed[LatexArgs.CommandEnd(trimmed, 0)..].Trim();
        return rest.Length == 0 || rest[0] is '\\' or '{' or '[';
    }

    private static bool ContinuesCommand(string trimmed) => trimmed[0] is '\\' or '{' or '[' or '}' or ']';

    private static int BraceDepth(string line)
    {
        var depth = 0;
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '\\')
            {
                i++;
            }
            else if (line[i] is '{' or '[')
            {
                depth++;
            }
            else if (line[i] is '}' or ']')
            {
                depth--;
            }
            i++;
        }
        return depth;
    }

    private static string FirstBrace(string text)
    {
        var open = text.IndexOf('{', StringComparison.Ordinal);
        var end = open < 0 ? -1 : LatexArgs.GroupEnd(text, open);
        return end < 0 ? "" : text[(open + 1)..(end - 1)].Replace('\n', ' ').Trim();
    }

    private static string CommandName(string source)
    {
        var end = 1;
        while (end < source.Length && char.IsAsciiLetter(source[end]))
        {
            end++;
        }
        return source[1..end];
    }

    // A line that is a Markdown fence would close the {=latex} fence early, so a space goes in front of it.
    private static string Fence(IEnumerable<string> lines) =>
        "```{=latex}\n" + string.Join('\n', lines.Select(l => l.TrimEnd(' ', '\t')).Select(l => l.StartsWith("```", StringComparison.Ordinal) ? " " + l : l)) + "\n```";

    private static string Excerpt(string text) => text.Length <= 80 ? text : text[..80] + "…";

    private void Count(string rule) => _mapped[rule] = _mapped.GetValueOrDefault(rule) + 1;

    [GeneratedRegex(@"^\s*\\begin\{(DndReadAloud|DndSidebar|DndComment)\}(.*)$")]
    private static partial Regex Container();

    [GeneratedRegex(@"^\s*\\begin\{(figure\*?)\}")]
    private static partial Regex Figure();

    [GeneratedRegex(@"\\DndDropCapLine\{([^{}]*)\}")]
    private static partial Regex DropCapCommand();

    [GeneratedRegex(@"^(#{1,6}) +(\d+(?:\.\d+)+)\.? +(\S.*)$")]
    private static partial Regex HeadingNumber();

    [GeneratedRegex("\uE000(\\d+)\uE001")]
    private static partial Regex SpanPlaceholder();
}
