namespace Codex.Contracts.Structure;

// PUT /api/scenes/{id}/position: put the scene in ChapterId (a live chapter of the same book) after AfterId, which must
// be another live scene of that chapter; null puts it first.
public sealed record MoveSceneRequest(Guid ChapterId, Guid? AfterId);
