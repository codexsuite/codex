namespace Codex.Contracts.Structure;

// PATCH /api/scenes/{id}: a field left out (null) is unchanged; an empty Title or Summary clears it.
// Status is one of empty, outline, beats, draft, revised, final.
// PlanNotes is the scene's planning notes (purpose, covers, boundaries, sources); null leaves it, empty clears it.
public sealed record PatchSceneRequest(string? Summary, string? Title, string? Status, string? PlanNotes = null);
