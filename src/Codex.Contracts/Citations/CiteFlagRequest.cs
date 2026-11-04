namespace Codex.Contracts.Citations;

// POST /api/quote-flags/{flagId}/cite: cite a possible unflagged quote. The flag's words are wrapped in the scene's
// current prose as a source marker for SourceId, or for the flag's own best-guess source when SourceId is null. Kind
// defaults to quote; the locator is the flag's ("p. 112").
public sealed record CiteFlagRequest(Guid? SourceId = null, string? Kind = null);
