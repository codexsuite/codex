namespace Codex.Contracts.References;

// PATCH /api/reference-uses/{id}. Kind is quote, paraphrase, data, figure or lyric.
public sealed record PatchUseRequest(string Kind);
