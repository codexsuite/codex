namespace Codex.Contracts.Ai.Integrations;

// ai_integration.kind. An OpenAI-compatible gateway takes a base URL; OpenRouter and Anthropic have fixed ones.
public static class AiIntegrationKinds
{
    public const string OpenAiCompatible = "openai_compatible";
    public const string OpenRouter = "openrouter";
    public const string Anthropic = "anthropic";

    public static readonly IReadOnlyList<string> All = [OpenAiCompatible, OpenRouter, Anthropic];
}
