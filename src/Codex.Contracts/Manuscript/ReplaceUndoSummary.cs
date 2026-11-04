namespace Codex.Contracts.Manuscript;

public sealed record ReplaceUndoSummary(Guid SceneId, string? Text);
