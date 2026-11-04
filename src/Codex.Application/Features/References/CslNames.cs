using System.Text.Json;

namespace Codex.Application.Features.References;

// Reads the parts of a CSL-JSON item the table and the exports share.
public static class CslNames
{
    public static IReadOnlyList<(string? Family, string? Given, string? Literal)> Authors(JsonElement csl) =>
        csl.TryGetProperty("author", out var authors) ? Names(authors) : [];

    public static IReadOnlyList<(string? Family, string? Given, string? Literal)> Names(JsonElement authors) =>
        authors.ValueKind != JsonValueKind.Array
            ? []
            : authors.EnumerateArray().Select(a => (Text(a, "family"), Text(a, "given"), Text(a, "literal"))).ToList();

    // "Beck", "Cagan and Jones", "Forsgren et al."
    public static string Label(string authorsJson)
    {
        using var document = JsonDocument.Parse(authorsJson);
        var names = Names(document.RootElement).Select(n => n.Literal ?? n.Family ?? n.Given ?? string.Empty).ToList();
        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]} et al.",
        };
    }

    public static int? Year(JsonElement csl) => DatePart(csl, "issued", 0);

    public static string? Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    public static (int Year, int Month, int Day)? Accessed(JsonElement csl)
    {
        var year = DatePart(csl, "accessed", 0);
        var month = DatePart(csl, "accessed", 1);
        var day = DatePart(csl, "accessed", 2);
        return year is null || month is null || day is null ? null : (year.Value, month.Value, day.Value);
    }

    private static int? DatePart(JsonElement csl, string property, int index)
    {
        if (!csl.TryGetProperty(property, out var date)
            || !date.TryGetProperty("date-parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array
            || parts.GetArrayLength() == 0)
        {
            return null;
        }

        var first = parts[0];
        return first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > index
            && first[index].ValueKind == JsonValueKind.Number
                ? first[index].GetInt32()
                : null;
    }
}
