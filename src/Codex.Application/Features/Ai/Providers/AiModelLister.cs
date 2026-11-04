using System.Text.Json;
using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Ai.Integrations;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Features.Ai.Providers;

// Reads a provider's model ids (`GET {base}/models`, the OpenAI shape `{ data: [{ id }] }` that OpenRouter and
// Anthropic share). Same guard, retry and error mapping as a draft; the answer is size-capped, sorted and distinct.
public sealed class AiModelLister(IHttpClientFactory clients, IAiEndpointGuard guard, ILogger<AiModelLister> logger, ChatCompletionsLimits? limits = null)
{
    public const string AnthropicVersion = "2023-06-01";

    private readonly ChatCompletionsLimits _limits = limits ?? ChatCompletionsLimits.Default;

    public async Task<IReadOnlyList<string>> ListAsync(AiConnection connection, CancellationToken cancellationToken)
    {
        var anthropic = connection.Kind == AiIntegrationKinds.Anthropic;
        var endpoint = anthropic
            ? new ProviderEndpoint("Anthropic", AnthropicProseGenerator.HttpClientName, ProviderEndpoint.AnthropicBase, false, new Dictionary<string, string>())
            : ProviderEndpoint.For(connection);

        using var response = await ProviderHttp.SendAsync(clients, guard, endpoint, () => Build(endpoint, connection, anthropic), _limits, logger, cancellationToken);
        var text = await ReadCappedAsync(response, endpoint.Name, cancellationToken);
        return Parse(text, endpoint.Name);
    }

    private static HttpRequestMessage Build(ProviderEndpoint endpoint, AiConnection connection, bool anthropic)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, endpoint.Url(anthropic ? "models?limit=1000" : "models"));
        if (anthropic)
        {
            message.Headers.TryAddWithoutValidation("x-api-key", connection.ApiKey);
            message.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            return message;
        }

        if (connection.ApiKey.Length > 0)
        {
            message.Headers.Authorization = new("Bearer", connection.ApiKey);
        }

        foreach (var (name, value) in endpoint.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        return message;
    }

    private async Task<string> ReadCappedAsync(HttpResponseMessage response, string provider, CancellationToken cancellationToken)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(_limits.IdleTimeout);
            await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
            var buffer = new byte[_limits.MaxResponseBytes + 1];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, limit.Token);
            return read > _limits.MaxResponseBytes
                ? throw ProviderFailures.Unavailable($"{provider} sent more than this app accepts.")
                : System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderFailures.ToException(provider, ProviderFailure.Unreachable);
        }
        catch (IOException)
        {
            throw ProviderFailures.ToException(provider, ProviderFailure.Unreachable);
        }
    }

    private static List<string> Parse(string text, string provider)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw ProviderFailures.Unavailable($"{provider} sent a model list this app could not read.");
            }

            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return data.EnumerateArray()
                .Select(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
                .OfType<string>()
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (JsonException)
        {
            throw ProviderFailures.Unavailable($"{provider} sent a model list this app could not read.");
        }
    }
}
