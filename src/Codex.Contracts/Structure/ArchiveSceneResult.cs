namespace Codex.Contracts.Structure;

// POST /api/scenes/{id}/archive: the uses that dropped out of the reference outline with the scene.
public sealed record ArchiveSceneResult(Guid SceneId, IReadOnlyList<SceneUseDto> ArchivedUses);
