namespace Codex.Contracts.Structure;

// An act's or chapter's opener art (#13), for the Plan header and Write's markers: the image, its caption, and the
// web rendition's URL (null while the worker has not made it, or when the image is archived).
public sealed record OutlineOpenerDto(Guid AssetId, string? Caption, string? WebUrl);
