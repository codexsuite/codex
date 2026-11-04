using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Codex.Application.Features.References;

// CSL-JSON items as RIS records: `XX  - value` lines, CRLF, each record closed by `ER  - `.
public static class RisWriter
{
    private const string NewLine = "\r\n";

    public static string Write(IEnumerable<JsonElement> items)
    {
        var text = new StringBuilder();
        foreach (var item in items)
        {
            AppendRecord(text, item);
        }

        return text.ToString();
    }

    private static void AppendRecord(StringBuilder text, JsonElement item)
    {
        var type = CslNames.Text(item, "type") switch
        {
            "book" => "BOOK",
            "article-journal" => "JOUR",
            "webpage" => "ELEC",
            "report" => "RPRT",
            "song" => "SOUND",
            _ => "GEN",
        };
        Line(text, "TY", type);
        Line(text, "ID", CslNames.Text(item, "id"));
        foreach (var name in CslNames.Authors(item))
        {
            Line(text, "AU", name.Literal ?? string.Join(", ", new[] { name.Family, name.Given }.Where(p => p is not null)));
        }

        Line(text, "TI", CslNames.Text(item, "title"));
        Line(text, "PY", CslNames.Year(item)?.ToString(CultureInfo.InvariantCulture));
        Line(text, "PB", CslNames.Text(item, "publisher"));
        Line(text, "T2", CslNames.Text(item, "container-title"));
        Line(text, "VL", CslNames.Text(item, "volume"));
        Line(text, "IS", CslNames.Text(item, "issue"));
        var pages = CslNames.Text(item, "page")?.Split(['–', '-'], 2, StringSplitOptions.TrimEntries);
        Line(text, "SP", pages?[0]);
        Line(text, "EP", pages is { Length: 2 } ? pages[1] : null);
        Line(text, "ET", CslNames.Text(item, "edition"));
        Line(text, "SN", CslNames.Text(item, "ISBN"));
        Line(text, "DO", CslNames.Text(item, "DOI"));
        Line(text, "UR", CslNames.Text(item, "URL"));
        if (CslNames.Accessed(item) is var (year, month, day))
        {
            Line(text, "Y2", $"{year:0000}/{month:00}/{day:00}/");
        }

        text.Append("ER  - ").Append(NewLine).Append(NewLine);
    }

    private static void Line(StringBuilder text, string tag, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            // A line break in a value would start a new tag line, so it becomes a space.
            var oneLine = value.ReplaceLineEndings(" ");
            text.Append(tag).Append("  - ").Append(oneLine).Append(NewLine);
        }
    }
}
