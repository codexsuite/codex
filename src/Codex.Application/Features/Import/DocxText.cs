using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Codex.Application.Features.Import;

// A Word paragraph as text. Plain is for titles; Markdown keeps italics and bold (direct formatting, or the Emphasis and
// Strong character styles), escapes every character Markdown would read as markup so ImportProse.ToStored reads the
// text back as typed, and puts each soft line break on its own line. Runs inside drawings and text boxes are not text
// of the paragraph.
internal static partial class DocxText
{
    public static string Plain(Paragraph paragraph) =>
        Spaces().Replace(string.Concat(Runs(paragraph).Select(RunText)), " ").Trim();

    public static string Markdown(Paragraph paragraph)
    {
        var output = new StringBuilder();
        var group = new StringBuilder();
        var marks = (Bold: false, Italic: false);
        foreach (var run in Runs(paragraph))
        {
            var text = RunText(run);
            if (text.Length == 0)
            {
                continue;
            }
            var runMarks = MarksOf(run);
            if (runMarks != marks)
            {
                Flush(output, group, marks);
                marks = runMarks;
            }
            group.Append(text);
        }
        Flush(output, group, marks);
        return string.Join('\n', output.ToString().Split('\n').Select(line => LineStart(line.Trim())).Where(line => line.Length > 0));
    }

    private static IEnumerable<Run> Runs(Paragraph paragraph) =>
        paragraph.Descendants<Run>().Where(r => !r.Ancestors<Run>().Any());

    private static string RunText(Run run)
    {
        var text = new StringBuilder();
        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text t:
                    text.Append(t.Text);
                    break;
                case TabChar:
                    text.Append(' ');
                    break;
                case NoBreakHyphen:
                    text.Append('-');
                    break;
                case CarriageReturn:
                    text.Append('\n');
                    break;
                case Break b when b.Type == null || b.Type.Value == BreakValues.TextWrapping:
                    text.Append('\n');
                    break;
            }
        }
        return text.ToString();
    }

    private static (bool Bold, bool Italic) MarksOf(Run run)
    {
        var properties = run.RunProperties;
        var style = properties?.RunStyle?.Val?.Value;
        return (
            On(properties?.Bold) || string.Equals(style, "Strong", StringComparison.OrdinalIgnoreCase),
            On(properties?.Italic) || string.Equals(style, "Emphasis", StringComparison.OrdinalIgnoreCase));
    }

    private static bool On(OnOffType? element) => element != null && (element.Val == null || element.Val.Value);

    // The text of one run of equal marks. Spaces stay outside the markers and each line is marked on its own, since
    // emphasis does not span a line break in the stored format.
    private static void Flush(StringBuilder output, StringBuilder group, (bool Bold, bool Italic) marks)
    {
        if (group.Length == 0)
        {
            return;
        }
        var lines = group.ToString().Split('\n');
        group.Clear();
        var marker = (marks.Bold ? "**" : "") + (marks.Italic ? "*" : "");
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                output.Append('\n');
            }
            var line = lines[i];
            var core = line.Trim();
            if (core.Length == 0 || marker.Length == 0)
            {
                output.Append(Escape(line));
                continue;
            }
            var lead = line[..(line.Length - line.TrimStart().Length)];
            var trail = line[line.TrimEnd().Length..];
            if (lead.Length == 0 && output.Length > 0 && output[^1] == '*')
            {
                // Touching the star that ends the group before it, an opening mark cannot pair: keep the text, drop the mark.
                output.Append(Escape(line));
                continue;
            }
            output.Append(lead).Append(marker).Append(Escape(core)).Append(marker).Append(trail);
        }
    }

    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']')
            {
                escaped.Append('\\');
            }
            escaped.Append(c);
        }
        return escaped.ToString();
    }

    // A line that would start another block in Markdown ("# ", "> ", "- ", "+ ", "1. ", "::") is escaped at its start;
    // a line of hyphens is left alone, since ToStored turns it into the scene break it means.
    private static string LineStart(string line)
    {
        if (line.Length == 0 || HyphenBreak().IsMatch(line))
        {
            return line;
        }
        if (line[0] is '#' or '>' or '-' or '+' || line.StartsWith("::", StringComparison.Ordinal))
        {
            return "\\" + line;
        }
        var ordered = OrderedStart().Match(line);
        return ordered.Success ? $"{ordered.Groups[1].Value}\\{line[ordered.Groups[1].Length..]}" : line;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^-(?: *-){2,}$")]
    private static partial Regex HyphenBreak();

    [GeneratedRegex(@"^(\d+)[.)](?: |$)")]
    private static partial Regex OrderedStart();
}
