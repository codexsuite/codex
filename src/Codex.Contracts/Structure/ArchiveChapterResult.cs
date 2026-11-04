namespace Codex.Contracts.Structure;

// POST /api/chapters/{id}/archive ("Delete chapter"): how many live scenes left the board with the chapter, and the
// uses that dropped out of the reference outline with them.
public sealed record ArchiveChapterResult(Guid ChapterId, int SceneCount, IReadOnlyList<SceneUseDto> ArchivedUses);
