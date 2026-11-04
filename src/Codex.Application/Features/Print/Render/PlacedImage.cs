namespace Codex.Application.Features.Print.Render;

// An image the document places: Source is the path inside the job folder (Pdf) or the rendition URL (Html).
public sealed record PlacedImage(Guid AssetId, string Source, RenderImage Rendition);
