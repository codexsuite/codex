namespace Codex.Application.Features.Print;

// A file offered to IAssetService.UploadAsync. Source, Prompt and Licence are optional notes kept in asset.meta.
public sealed record AssetUpload(string FileName, string ContentType, long Length, Stream Content, string? Source = null, string? Prompt = null, string? Licence = null);
