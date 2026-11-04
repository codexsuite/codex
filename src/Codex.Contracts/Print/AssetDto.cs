namespace Codex.Contracts.Print;

// An uploaded file. Status is the rendition job: "processing" (queued or running), "ready" (all three renditions
// exist), "failed" (Error says why; POST /api/assets/{id}/renditions retries) or "none" (never queued, e.g. a seed
// row). Scope is "book" for an asset owned by the book asked about, "series" for one shared from its series.
// Dpi is set only when the request named a trim.
public sealed record AssetDto(
    Guid Id,
    Guid? BookId,
    string Scope,
    string Kind,
    string Filename,
    string Mime,
    long Bytes,
    int? WidthPx,
    int? HeightPx,
    string Status,
    string? Error,
    string Url,
    IReadOnlyList<AssetRenditionDto> Renditions,
    AssetDpiDto? Dpi,
    DateTimeOffset CreatedAt,
    string CreatedLabel);
