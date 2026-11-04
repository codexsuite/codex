namespace Codex.Application.Features.Print;

// A template's text block on the page, in inches: margins (inner is the binding side), the gap between columns and
// how many columns it sets. Each template package declares its own (template.json "page"); Default is the house
// layout for callers that have no template in hand, such as an asset's dpi before the book has picked one.
public sealed record PageLayout(double Top, double Bottom, double Inner, double Outer, double ColumnGap, int Columns)
{
    public static readonly PageLayout Default = new(0.75, 0.75, 0.75, 0.75, 0.25, 1);
}
