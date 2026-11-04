namespace Codex.Contracts.Ai.Integrations;

// GET /api/ai/integrations: the Settings → AI page. LocalEndpointsAllowed is the server owner's
// CODEX_AI_ALLOW_LOCAL_ENDPOINTS switch (http:// and private-network base URLs, e.g. Ollama on the LAN).
public sealed record AiSettingsDto(IReadOnlyList<AiIntegrationDto> Integrations, bool LocalEndpointsAllowed);
