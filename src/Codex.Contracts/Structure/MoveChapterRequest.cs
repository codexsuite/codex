namespace Codex.Contracts.Structure;

// PUT /api/chapters/{id}/position: put the chapter in ActId (a live act of the same book) after AfterId, which must be
// another live chapter of that act; null puts it first.
public sealed record MoveChapterRequest(Guid ActId, Guid? AfterId);
