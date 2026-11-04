namespace Codex.Application.Features.Print.Render;

// The rendition of an asset for the request's ImagePurpose. An asset with no row here is reported missing.
public sealed record RenderImage(Guid AssetId, string Purpose, string Mime, int WidthPx, int HeightPx, string StorageKey)
{
    public string Extension => Mime switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin",
    };
}
