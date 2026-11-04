namespace Codex.Contracts.Ai.Integrations;

// POST /api/ai/integrations -> AiIntegrationDto. BaseUrl is required for openai_compatible and refused otherwise.
// The user's first integration becomes their default.
public sealed record CreateAiIntegrationRequest(string Kind, string Label, string? BaseUrl, string ApiKey, string? DefaultModel);
