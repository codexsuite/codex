namespace Codex.Contracts.Structure;

// PATCH /api/acts/{id}: renames the act; an empty Title leaves it untitled ("Name this act").
public sealed record PatchActRequest(string? Title);
