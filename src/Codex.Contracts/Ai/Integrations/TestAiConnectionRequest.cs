namespace Codex.Contracts.Ai.Integrations;

// POST /api/ai/integrations/test -> AiConnectionTestDto: tries a connection before it is saved (the Connect dialog).
// A saved integration is tested with POST /api/ai/integrations/{id}/test (no body), which uses the stored key.
public sealed record TestAiConnectionRequest(string Kind, string? BaseUrl, string ApiKey);
