namespace Codex.Contracts.Print;

// The effective resolution of an image when placed in a trim size: pixels across the placed width divided by that
// width in inches. Status is "ok" when EffectiveDpi reaches TargetDpi (300 print, 150 screen), otherwise "low".
public sealed record AssetDpiDto(
    string Trim,
    int Columns,
    string Placement,
    double PlacedWidthIn,
    double PlacedHeightIn,
    double EffectiveDpi,
    int TargetDpi,
    string Status);
