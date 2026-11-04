namespace Codex.Contracts.Ai;

// The scene after an apply or an undo-apply: its new version and whole body.
public sealed record DraftApplyResult(int Version, string Body);
