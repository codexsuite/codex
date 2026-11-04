namespace Codex.Contracts.Structure;

// POST /api/chapters/{id}/scenes: Title is optional. The scene goes after AfterId, which must be a live scene of that
// chapter; null appends it to the end of the chapter.
public sealed record CreateSceneRequest(string? Title, Guid? AfterId);
