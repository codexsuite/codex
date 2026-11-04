using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Features.Ai.Providers;

// Streams a draft from a Chat Completions endpoint (`POST {base}/chat/completions`, SSE), for the openai_compatible
// and openrouter kinds: they differ only in base URL and headers (ProviderEndpoint). The key goes in the bearer header
// and nowhere else. Stop is the caller's cancellation (partial text stands). Failures before the first chunk are retried
// by ProviderHttp; once text has been yielded nothing is replayed. A silent stream or an oversized response ends the
// draft with a fixed message rather than hanging; the size cap counts bytes as they arrive (SseLineReader), so a
// line without an end cannot grow without bound. Lines that are not objects are keep-alives and are skipped.
public sealed class ChatCompletionsProseGenerator(
    IHttpClientFactory clients,
    IAiEndpointGuard guard,
    AiConnection connection,
    ILogger<ChatCompletionsProseGenerator> logger,
    ChatCompletionsLimits? limits = null) : IProseGenerator
{
    private readonly ChatCompletionsLimits _limits = limits ?? ChatCompletionsLimits.Default;

    public async IAsyncEnumerable<ProseChunk> GenerateAsync(ProseRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endpoint = ProviderEndpoint.For(connection);
        using var response = await ProviderHttp.SendAsync(clients, guard, endpoint, () => Build(endpoint, request), _limits, logger, cancellationToken);
        await using var reader = new SseLineReader(await response.Content.ReadAsStreamAsync(cancellationToken), _limits.MaxResponseBytes, endpoint.Name);

        var inputTokens = 0;
        var outputTokens = 0;
        var finished = false;
        while (true)
        {
            var line = await ReadLineAsync(reader, endpoint.Name, cancellationToken);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            if (data == "[DONE]")
            {
                finished = true;
                break;
            }

            using var document = Parse(data, endpoint.Name);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                continue; // a keep-alive such as data: "ping"
            }

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                logger.LogWarning("{Provider} ended the stream with an error object", endpoint.Name);
                throw ProviderFailures.Unavailable($"{endpoint.Name} stopped the draft with an error. Try again in a moment.");
            }

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = Count(usage, "prompt_tokens", inputTokens);
                outputTokens = Count(usage, "completion_tokens", outputTokens);
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object
                && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                && content.GetString() is { Length: > 0 } text)
            {
                yield return new ProseChunk(text);
            }

            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                finished = true;
                RequireNormalEnd(reason.GetString(), endpoint.Name);
            }
        }

        if (!finished)
        {
            throw ProviderFailures.ToException(endpoint.Name, ProviderFailure.Unreachable);
        }

        yield return ProseChunk.Done(new ProseResult(request.Model, inputTokens, outputTokens));
    }

    private HttpRequestMessage Build(ProviderEndpoint endpoint, ProseRequest request)
    {
        var messages = new JsonArray();
        if (request.Instruction.Length > 0)
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = request.Instruction });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.Prompt });
        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
        };

        var message = new HttpRequestMessage(HttpMethod.Post, endpoint.Url("chat/completions"))
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Accept.ParseAdd("text/event-stream");
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

    // One line, or null at the end of the stream. Silence longer than the idle limit ends the draft; the caller's own
    // cancellation is left to propagate (Stop).
    private async Task<string?> ReadLineAsync(SseLineReader reader, string provider, CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(_limits.IdleTimeout);
        try
        {
            return await reader.ReadLineAsync(idle.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderFailures.Unavailable($"{provider} stopped answering. Try again in a moment.");
        }
        catch (IOException)
        {
            throw ProviderFailures.ToException(provider, ProviderFailure.Unreachable);
        }
    }

    private static JsonDocument Parse(string data, string provider)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException)
        {
            throw ProviderFailures.Unavailable($"{provider} sent an answer this app could not read. Try again in a moment.");
        }
    }

    private static int Count(JsonElement usage, string name, int fallback) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) ? count : fallback;

    private static void RequireNormalEnd(string? reason, string provider)
    {
        if (reason == "length")
        {
            throw ProviderFailures.Unavailable("The draft hit the length limit before it finished. Lower the target length and try again.");
        }

        if (reason == "content_filter")
        {
            throw ApiException.Blocked($"{provider} declined to write this passage. Reword the beat and try again.");
        }
    }
}
