namespace Codex.Contracts.Print;

// A `::figure{id}` line's row, ready for Write's block card. Missing is true when the figure's image is gone (the asset
// is archived). WebUrl is the asset's web rendition, null while the worker has not made it. Dpi is the effective
// resolution of the image where the figure is placed, judged against the book's current layout (null without pixel size).
public sealed record FigureDto(
    Guid Id,
    Guid SceneId,
    Guid AssetId,
    string Placement,
    string? Caption,
    string? AltText,
    bool Missing,
    string? WebUrl,
    int? WidthPx,
    int? HeightPx,
    AssetDpiDto? Dpi);
