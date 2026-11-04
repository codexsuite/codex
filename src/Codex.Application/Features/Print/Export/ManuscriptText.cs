using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codex.Application.Features.Print.Export;

// Turns scene bodies (docs/prose-format.md) into Markdown for a reader: no directives, no kb:/chapter:/ref: links.
// One instance per document: it numbers footnotes in reading order and counts what it leaves out.
// - ::beat lines become a block quote "Beat: …" when beats are included, else go; ::kb-addition, ::spell, ::statblock,
//   ::pagebreak and unknown directives go; ::figure lines go and are counted (images are not exported).
// - ```{=latex} blocks go and are counted. Containers become block quotes.
// - [x](kb:…) and [x](chapter:…) become x. A source link keeps its words and gets a footnote (Chicago) or an
//   "(Author Year, locator)" (APA, Harvard) the first time its use appears.
// - Headings inside a scene move down two levels, so only the export's own act and chapter headings are at level 1 and 2.
public sealed partial class ManuscriptText(string citationStyle, IReadOnlyDictionary<Guid, ManuscriptCitation> citations, IReadOnlyDictionary<Guid, string> beats, bool includeBeats)
{
    private readonly List<string> _footnotes = [];
    private readonly HashSet<Guid> _seenUses = [];
    private readonly Dictionary<Guid, ManuscriptCitation> _cited = [];

    public int RawLatexSkipped { get; private set; }

    public int FiguresSkipped { get; private set; }

    public int Footnotes => _footnotes.Count;

    [GeneratedRegex("\\[((?:[^\\[\\]\\\\]|\\\\.)*)\\]\\((kb|chapter|ref):([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\\)")]
    private static partial Regex Link();

    [GeneratedRegex("^::([a-z][a-z-]*)(\\{[^}\\n]*\\})?[ \\t]*$")]
    private static partial Regex Directive();

    [GeneratedRegex("id=\"([0-9a-fA-F-]{36})\"")]
    private static partial Regex DirectiveId();

    [GeneratedRegex("^:::[a-z]+(?:\\{title=\"([^\"]*)\"\\})?[ \\t]*$")]
    private static partial Regex ContainerOpen();

    [GeneratedRegex("^(#{1,6}) (.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex BlankRuns();

    public string Scene(string body)
    {
        var output = new List<string>();
        var inFence = false;
        var dropFence = false;
        var inContainer = false;
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (inFence)
            {
                inFence = line != "```";
                if (!dropFence)
                {
                    output.Add(line);
                }

                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = true;
                dropFence = line.StartsWith("```{=latex}", StringComparison.Ordinal);
                RawLatexSkipped += dropFence ? 1 : 0;
                if (!dropFence)
                {
                    output.Add(line);
                }

                continue;
            }

            if (line == ":::")
            {
                inContainer = false;
                output.Add(string.Empty);
                continue;
            }

            if (ContainerOpen().Match(line) is { Success: true } open)
            {
                inContainer = true;
                if (open.Groups[1].Success && open.Groups[1].Value.Length > 0)
                {
                    output.Add($"> **{open.Groups[1].Value}**");
                    output.Add(">");
                }

                continue;
            }

            if (line.StartsWith("::", StringComparison.Ordinal))
            {
                output.AddRange(DirectiveLines(line));
                continue;
            }

            var text = Inline(line);
            if (Heading().Match(text) is { Success: true } heading)
            {
                text = new string('#', Math.Min(6, heading.Groups[1].Length + 2)) + " " + heading.Groups[2].Value;
            }

            if (inContainer)
            {
                text = text.Length == 0 ? ">" : "> " + text;
            }

            output.Add(text);
        }

        return BlankRuns().Replace(string.Join('\n', output), "\n\n").Trim('\n');
    }

    // The scene's summary as one italic paragraph (links reduced to their words).
    public static string Summary(string summary)
    {
        var words = Link().Replace(summary.Replace('\n', ' '), m => m.Groups[1].Value).Replace("*", string.Empty).Trim();
        return words.Length == 0 ? string.Empty : $"*{words}*";
    }

    public string FootnoteDefinitions() => string.Join("\n\n", _footnotes.Select((text, i) => $"[^{i + 1}]: {text}"));

    // A "Bibliography" section of the sources cited so far, or empty.
    public string Bibliography()
    {
        if (_cited.Count == 0)
        {
            return string.Empty;
        }

        var entries = _cited.Values
            .Select(c => (Key: Authors(c.Authors, full: true) + " " + c.Title, Text: Entry(c)))
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.Text);
        return "# Bibliography\n\n" + string.Join("\n\n", entries);
    }

    private IEnumerable<string> DirectiveLines(string line)
    {
        var directive = Directive().Match(line);
        if (!directive.Success)
        {
            return [];
        }

        switch (directive.Groups[1].Value)
        {
            case "beat" when includeBeats && BeatId(directive) is { } id && beats.TryGetValue(id, out var instruction) && instruction.Trim().Length > 0:
                var lines = instruction.Trim().Replace("\r\n", "\n").Split('\n');
                return lines.Select((l, i) => i == 0 ? $"> Beat: {l}" : Quote(l));
            case "figure":
                FiguresSkipped++;
                return [];
            default:
                return [];
        }
    }

    private static string Quote(string line) => line.Length == 0 ? ">" : "> " + line;

    private static Guid? BeatId(Match directive) =>
        DirectiveId().Match(directive.Groups[2].Value) is { Success: true } m && Guid.TryParse(m.Groups[1].Value, out var id) ? id : null;

    private string Inline(string line) => Link().Replace(line, m =>
    {
        var words = m.Groups[1].Value;
        if (m.Groups[2].Value != "ref")
        {
            return words;
        }

        return (words == "^" ? string.Empty : words) + Marker(Guid.Parse(m.Groups[3].Value));
    });

    private string Marker(Guid useId)
    {
        if (!citations.TryGetValue(useId, out var cite) || !_seenUses.Add(useId))
        {
            return string.Empty;
        }

        _cited[cite.ReferenceId] = cite;
        if (citationStyle == "chicago")
        {
            _footnotes.Add(Footnote(cite));
            return $"[^{_footnotes.Count}]";
        }

        var year = cite.Year?.ToString(CultureInfo.InvariantCulture);
        var inner = string.Join(", ", new[] { string.Join(' ', new[] { Authors(cite.Authors, full: false), year }.Where(p => !string.IsNullOrEmpty(p))), cite.Locator }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        return inner.Length == 0 ? string.Empty : $" ({Escape(inner)})";
    }

    // Chicago note: Author, *Title* (Publisher, Year), locator.
    private static string Footnote(ManuscriptCitation cite)
    {
        var text = new StringBuilder();
        var authors = Authors(cite.Authors, full: false);
        if (authors.Length > 0)
        {
            text.Append(Escape(authors)).Append(", ");
        }

        text.Append('*').Append(Escape(cite.Title.Length > 0 ? cite.Title : "Untitled source")).Append('*');
        var published = string.Join(", ", new[] { cite.Publisher, cite.Year?.ToString(CultureInfo.InvariantCulture) }.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (published.Length > 0)
        {
            text.Append(" (").Append(Escape(published)).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(cite.Locator))
        {
            text.Append(", ").Append(Escape(cite.Locator));
        }

        return text.Append('.').ToString();
    }

    // Author. *Title*. Publisher, Year.
    private static string Entry(ManuscriptCitation cite)
    {
        var parts = new List<string>();
        var authors = Authors(cite.Authors, full: true);
        if (authors.Length > 0)
        {
            parts.Add(Escape(authors.TrimEnd('.')) + ".");
        }

        parts.Add($"*{Escape(cite.Title.Length > 0 ? cite.Title : "Untitled source")}*.");
        var published = string.Join(", ", new[] { cite.Publisher, cite.Year?.ToString(CultureInfo.InvariantCulture) }.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (published.Length > 0)
        {
            parts.Add(Escape(published) + ".");
        }

        return string.Join(' ', parts);
    }

    // full: "Beck, Kent; Cagan, Marty"; else the short label "Beck", "Cagan and Jones", "Forsgren et al.".
    private static string Authors(string authorsJson, bool full)
    {
        using var json = JsonDocument.Parse(authorsJson);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var names = json.RootElement.EnumerateArray().Select(a => Name(a, full)).OfType<string>().ToList();
        if (full)
        {
            return string.Join("; ", names);
        }

        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} et al.",
        };
    }

    private static string? Read(JsonElement name, string key) =>
        name.ValueKind == JsonValueKind.Object && name.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    private static string? Name(JsonElement author, bool full)
    {
        if (Read(author, "literal") is { } literal)
        {
            return literal;
        }

        var family = Read(author, "family");
        var given = Read(author, "given");
        if (!full || family is null)
        {
            return family ?? given;
        }

        return given is null ? family : $"{family}, {given}";
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("[", "\\[").Replace("]", "\\]");
}
