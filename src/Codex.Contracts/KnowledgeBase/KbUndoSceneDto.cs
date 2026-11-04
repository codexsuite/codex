namespace Codex.Contracts.KnowledgeBase;

// A scene body as it was before the delete; Version is the version the delete left the scene at.
public sealed record KbUndoSceneDto(Guid SceneId, int Version, string Body);
