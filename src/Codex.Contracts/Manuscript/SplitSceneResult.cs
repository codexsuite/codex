namespace Codex.Contracts.Manuscript;

public sealed record SplitSceneResult(Guid NewSceneId, int Version, int NewSceneVersion);
