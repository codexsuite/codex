namespace Codex.Contracts.Print;

// PUT /api/acts/{id}/opener and /api/chapters/{id}/opener: the part- or chapter-opener art the template draws (#13).
// AssetId is an image of the book or its series; null clears the opener and its caption.
public sealed record SetOpenerRequest(Guid? AssetId, string? Caption = null);
