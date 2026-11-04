namespace Codex.Contracts.Structure;

// PUT /api/acts/{id}/position: put the act after AfterId; null puts it first. AfterId must be another live act of the book.
public sealed record MoveActRequest(Guid? AfterId);
