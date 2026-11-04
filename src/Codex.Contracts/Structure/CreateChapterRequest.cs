namespace Codex.Contracts.Structure;

// POST /api/acts/{id}/chapters: Title defaults to "New chapter". The chapter goes after AfterId, which must be a live
// chapter of that act; null appends it to the end of the act.
public sealed record CreateChapterRequest(string? Title, Guid? AfterId);
