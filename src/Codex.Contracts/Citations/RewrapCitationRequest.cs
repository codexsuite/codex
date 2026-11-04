namespace Codex.Contracts.Citations;

// Moves an existing source marker onto new words: Passage is the stored Markdown of the new words, found in the scene's
// prose with the use's old marker taken off (the n-th match when Occurrence is given, 1-based). Version is the scene's
// prose version the passage was read at.
public sealed record RewrapCitationRequest(int Version, string Passage, int? Occurrence = null);
