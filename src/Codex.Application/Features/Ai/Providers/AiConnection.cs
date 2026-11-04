namespace Codex.Application.Features.Ai.Providers;

// What a provider needs to answer: the integration kind, its base URL (openai_compatible only) and the decrypted key.
// ToString leaves the key out, so a connection that reaches a log line or an exception message never carries it.
public sealed record AiConnection(string Kind, Uri? BaseUrl, string ApiKey)
{
    public override string ToString() => BaseUrl is null ? Kind : $"{Kind} {BaseUrl}";
}
