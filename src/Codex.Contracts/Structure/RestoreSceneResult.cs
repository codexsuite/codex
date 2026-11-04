namespace Codex.Contracts.Structure;

// POST /api/scenes/{id}/restore: the uses that are back in the reference outline.
public sealed record RestoreSceneResult(Guid SceneId, IReadOnlyList<SceneUseDto> RestoredUses);
