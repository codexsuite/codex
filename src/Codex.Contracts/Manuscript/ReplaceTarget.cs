namespace Codex.Contracts.Manuscript;

// One match to replace on its own: the scene, the source (prose, summary or beat) and the match's ordinal in it.
public sealed record ReplaceTarget(Guid SceneId, string Source, int Ordinal);
