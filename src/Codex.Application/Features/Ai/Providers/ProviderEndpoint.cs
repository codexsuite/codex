using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;

namespace Codex.Application.Features.Ai.Providers;

// Where a connection's requests go: the display name used in messages, the named HttpClient, the base URL and the
// fixed headers of that provider. Only openai_compatible carries a user-supplied URL, so only it needs the SSRF guard.
public sealed record ProviderEndpoint(string Name, string ClientName, Uri BaseUrl, bool NeedsGuard, IReadOnlyDictionary<string, string> Headers)
{
    public static readonly Uri OpenRouterBase = new("https://openrouter.ai/api/v1");
    public static readonly Uri AnthropicBase = new("https://api.anthropic.com/v1");

    public static ProviderEndpoint For(AiConnection connection) => connection.Kind switch
    {
        AiIntegrationKinds.OpenAiCompatible => new ProviderEndpoint(
            "The OpenAI-compatible provider",
            AiHttpClients.OpenAiCompatible,
            connection.BaseUrl ?? throw ApiException.Validation("This integration has no base URL."),
            true,
            new Dictionary<string, string>()),
        AiIntegrationKinds.OpenRouter => new ProviderEndpoint(
            "OpenRouter",
            AiHttpClients.OpenRouter,
            OpenRouterBase,
            false,
            new Dictionary<string, string> { ["HTTP-Referer"] = "https://github.com/codexsuite/codex", ["X-Title"] = "Codex" }),
        _ => throw ApiException.Validation($"'{connection.Kind}' is not a chat-completions integration."),
    };

    // {base}/{path}, tolerating a trailing slash on the base the user typed.
    public Uri Url(string path) => new($"{BaseUrl.AbsoluteUri.TrimEnd('/')}/{path}");
}
