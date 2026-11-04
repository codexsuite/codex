namespace Codex.Contracts.Structure;

// POST /api/scenes/{id}/merge-previous: the scene that took the text (the previous one), its new prose version and the
// scene that was archived (the one merged).
public sealed record MergeSceneResult(Guid SceneId, int Version, Guid ArchivedSceneId);
