namespace Codex.Application.Features.Print;

// The layout an effective dpi is judged for: the trim slug, the template's column count and the figure placement.
public sealed record AssetDpiRequest(string Trim, int Columns = 1, string Placement = "column", int TargetDpi = EffectiveDpi.PrintTarget);
