namespace Codex.Application.Features.Ai.Providers;

// The named HttpClients providers send through, so a test stubs the socket by name. Anthropic keeps
// AnthropicProseGenerator.HttpClientName ("anthropic").
public static class AiHttpClients
{
    public const string OpenAiCompatible = "ai-openai-compatible";
    public const string OpenRouter = "ai-openrouter";
}
