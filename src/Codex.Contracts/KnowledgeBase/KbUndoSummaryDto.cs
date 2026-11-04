namespace Codex.Contracts.KnowledgeBase;

// A scene summary as it was before the delete.
public sealed record KbUndoSummaryDto(Guid SceneId, string Summary);
