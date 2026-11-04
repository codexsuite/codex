namespace Codex.Contracts.Print;

// One derived file of an image asset. Purpose is web (WebP, 1600 px longest edge, Quick preview), proxy (150 dpi JPEG
// for proofs) or print (300 dpi at the largest placed size; PNG for line art, JPEG otherwise). Url serves the bytes.
public sealed record AssetRenditionDto(string Purpose, string Mime, int WidthPx, int HeightPx, long Bytes, string Url);
