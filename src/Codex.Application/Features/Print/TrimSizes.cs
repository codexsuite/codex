using Codex.Contracts.Errors;

namespace Codex.Application.Features.Print;

// Page geometry for the trims a template can offer (the slugs in a template manifest's "trims"), in inches.
// The text block is the page minus the template's margins (PageLayout, from its template.json); columns split it with
// the template's column gap between them. Without a layout the house default (PageLayout.Default) applies.
public static class TrimSizes
{
    public const int MaxColumns = 3;

    private const double MmPerInch = 25.4;

    private static readonly Dictionary<string, (double Width, double Height)> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["us-letter"] = (8.5, 11),
        ["8.5x11"] = (8.5, 11),
        ["a4"] = (210 / MmPerInch, 297 / MmPerInch),
        ["6x9"] = (6, 9),
        ["5.5x8.5"] = (5.5, 8.5),
    };

    public static IReadOnlyCollection<string> Known => Pages.Keys;

    public static bool IsKnown(string trim) => Pages.ContainsKey(trim);

    // Page width and height. Unknown trim: 400 validation.
    public static (double Width, double Height) Page(string trim)
        => Pages.TryGetValue(trim, out var page)
            ? page
            : throw ApiException.Validation($"Unknown trim size '{trim}'. Known trims: {string.Join(", ", Pages.Keys)}.");

    public static double TextWidth(string trim, PageLayout? layout = null)
    {
        var page = layout ?? PageLayout.Default;
        return Page(trim).Width - page.Inner - page.Outer;
    }

    public static double ColumnWidth(string trim, int columns, PageLayout? layout = null)
    {
        if (columns is < 1 or > MaxColumns)
        {
            throw ApiException.Validation($"Columns must be between 1 and {MaxColumns}.");
        }
        return (TextWidth(trim, layout) - ((columns - 1) * (layout ?? PageLayout.Default).ColumnGap)) / columns;
    }

    public static double TextHeight(string trim, PageLayout? layout = null)
    {
        var page = layout ?? PageLayout.Default;
        return Page(trim).Height - page.Top - page.Bottom;
    }

    // The box a figure placement may fill, in inches (width, height). #13 defines no placement that bleeds: `page` is the
    // float page inside the text block (figure[p]), `spread` the text blocks of two facing pages, `float-top` the text
    // width, `column` one column; every box is as tall as the text block.
    public static (double Width, double Height) PlacedBox(string trim, int columns, string placement, PageLayout? layout = null)
    {
        var height = TextHeight(trim, layout);
        return placement switch
        {
            "column" => (ColumnWidth(trim, columns, layout), height),
            "float-top" or "page" => (TextWidth(trim, layout), height),
            "spread" => (2 * TextWidth(trim, layout), height),
            _ => throw ApiException.Validation($"Unknown placement '{placement}'. Use column, page, spread or float-top."),
        };
    }
}
