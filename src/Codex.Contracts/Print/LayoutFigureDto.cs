namespace Codex.Contracts.Print;

// A figure of the book as this template and trim place it (block card). Status is "ok" or "low" against the 300 dpi
// print target (AssetDpiDto); Placement is the figure row's (column, page, spread, float-top).
public sealed record LayoutFigureDto(
    Guid FigureId,
    Guid AssetId,
    Guid SceneId,
    string Filename,
    string? Caption,
    string Placement,
    int? WidthPx,
    int? HeightPx,
    double? PlacedWidthIn,
    double? PlacedHeightIn,
    double? EffectiveDpi,
    string Status);
