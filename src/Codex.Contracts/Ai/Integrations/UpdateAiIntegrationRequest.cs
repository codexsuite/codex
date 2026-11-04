namespace Codex.Contracts.Ai.Integrations;

// PATCH /api/ai/integrations/{id} -> AiIntegrationDto. A null member is left as it is. ApiKey is write-only: a new
// value replaces the stored key, and nothing ever reads it back.
public sealed record UpdateAiIntegrationRequest(string? Label, string? BaseUrl, string? ApiKey, string? DefaultModel);
