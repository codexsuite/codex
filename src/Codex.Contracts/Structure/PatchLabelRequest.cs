namespace Codex.Contracts.Structure;

// PATCH /api/labels/{id}: a field left out (null) is unchanged; an empty Color clears it.
public sealed record PatchLabelRequest(string? Name, string? Color);
