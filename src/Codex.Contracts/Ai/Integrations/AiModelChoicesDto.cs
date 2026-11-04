namespace Codex.Contracts.Ai.Integrations;

// GET /api/ai/models: the Write draft panel's integration and model picker. Empty: nothing connected, and the panel
// shows "Connect an AI provider in Settings".
public sealed record AiModelChoicesDto(IReadOnlyList<AiModelChoiceDto> Integrations);
