namespace Codex.Application.Features.Print;

// What the agent says it will send: the file's name, type (PNG, JPEG or WebP) and size, plus the asset.meta notes.
public sealed record AssetUploadLinkRequest(
    string FileName, string ContentType, long Bytes, string? Source = null, string? Prompt = null, string? Licence = null);
