namespace Codex.Contracts.KnowledgeBase;

// POST /api/kb/entries/{id}/merge: the entry is merged into IntoId.
public sealed record KbMergeRequest(Guid IntoId);
