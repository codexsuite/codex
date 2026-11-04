namespace Codex.Contracts.Manuscript;

// GET /api/scenes/{id}/prose/revisions: the newest revisions first (at most 50), with the scene's current text, version
// and words so the panel can compare against it and tell when the text moved on elsewhere. The current text is not
// always a revision (an agent edit cuts its revision before the edit). An archived scene's history is read-only.
public sealed record ProseHistoryDto(
    Guid SceneId, bool Archived, int Version, int CurrentWords, string Body, IReadOnlyList<ProseRevisionDto> Revisions);
