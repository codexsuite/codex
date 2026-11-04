namespace Codex.Contracts.Manuscript;

// POST /api/scenes/{id}/prose/revisions/{revisionId}/restore. `PreviousRevisionId` is the revision that holds the text
// the restore replaced, so Undo is a restore of it.
public sealed record RestoreRevisionResult(int Version, string Body, Guid PreviousRevisionId);
