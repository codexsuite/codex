namespace Codex.Contracts.Ai;

// The scene version the caller last saw: apply and undo-apply answer 409 when the scene has moved on.
public sealed record DraftVersionRequest(int Version);
