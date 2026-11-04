namespace Codex.Application.Features.Print;

// An opened original or rendition; the caller disposes Content.
public sealed record AssetFile(Stream Content, string ContentType, string FileName);
