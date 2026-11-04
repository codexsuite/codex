using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Codex.Application.Features.References;

// CSL-JSON items as BibTeX entries.
public static class BibTexWriter
{
    public static string Write(IEnumerable<JsonElement> items)
    {
        var text = new StringBuilder();
        foreach (var item in items)
        {
            AppendEntry(text, item);
            text.Append('\n');
        }

        return text.ToString();
    }

    private static void AppendEntry(StringBuilder text, JsonElement item)
    {
        var type = CslNames.Text(item, "type");
        var entryType = type switch
        {
            "book" => "book",
            "article-journal" => "article",
            "report" => "techreport",
            _ => "misc",
        };
        text.Append('@').Append(entryType).Append('{').Append(CslNames.Text(item, "id")).Append(",\n");
        var authors = string.Join(" and ", CslNames.Authors(item).Select(Name));
        Field(text, "author", authors);
        Field(text, "title", CslNames.Text(item, "title"), protect: true);
        Field(text, "year", CslNames.Year(item)?.ToString(CultureInfo.InvariantCulture));
        Field(text, entryType == "techreport" ? "institution" : "publisher", CslNames.Text(item, "publisher"));
        Field(text, entryType == "article" ? "journal" : "booktitle", CslNames.Text(item, "container-title"));
        Field(text, "volume", CslNames.Text(item, "volume"));
        Field(text, "number", CslNames.Text(item, "issue"));
        Field(text, "pages", CslNames.Text(item, "page")?.Replace("–", "--", StringComparison.Ordinal));
        Field(text, "edition", CslNames.Text(item, "edition"));
        Field(text, "isbn", CslNames.Text(item, "ISBN"));
        Field(text, "doi", CslNames.Text(item, "DOI"));
        Field(text, "url", CslNames.Text(item, "URL"));
        if (CslNames.Accessed(item) is var (year, month, day))
        {
            Field(text, "urldate", $"{year:0000}-{month:00}-{day:00}");
        }

        text.Append("}\n");
    }

    private static string Name((string? Family, string? Given, string? Literal) name) =>
        name.Literal is not null
            ? "{" + Escape(name.Literal) + "}"
            : string.Join(", ", new[] { name.Family, name.Given }.Where(p => p is not null).Select(p => Escape(p!)));

    private static void Field(StringBuilder text, string name, string? value, bool protect = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var body = name == "author" ? value : Escape(value);
        text.Append("  ").Append(name).Append(" = {").Append(protect ? "{" + body + "}" : body).Append("},\n");
    }

    private static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var c in value.ReplaceLineEndings(" "))
        {
            if (c == '\\')
            {
                escaped.Append("\\textbackslash{}");
                continue;
            }

            if (c is '&' or '%' or '$' or '#' or '_' or '{' or '}')
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }
}
