using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models;
using Anthropic.Models.Messages;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Microsoft.Extensions.Logging;

namespace Codex.Application.Features.Ai;

// Streams a draft from the Anthropic Messages API. The HTTP socket is the named client `HttpClientName`, so a test
// stubs it; the key is held here only to build the SDK client and is never logged or put in a message.
// Stop is the caller's cancellation: it ends the stream with OperationCanceledException and the text already
// yielded stands. Every provider failure is logged (status, error type, request id) and becomes an ApiException with
// a fixed message the Write panel can show; the SDK exception is the logged one because ApiException carries no inner exception.
public sealed class AnthropicProseGenerator(IHttpClientFactory clients, string apiKey, ILogger<AnthropicProseGenerator> logger) : IProseGenerator
{
    public const string HttpClientName = "anthropic";

    private const int MaxTokens = 16000;
    private const int Overloaded = 529;
    private const int PaymentRequired = 402;

    public async IAsyncEnumerable<ProseChunk> GenerateAsync(ProseRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The SDK retries (twice) only until the response headers arrive, i.e. before any text has been shown;
        // once the SSE body is streaming a failure surfaces as an exception and is never replayed.
        using var client = new AnthropicClient
        {
            ApiKey = apiKey,
            HttpClient = clients.CreateClient(HttpClientName),
            MaxRetries = 2,
        };
        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = MaxTokens,
            Messages = [new() { Role = Role.User, Content = request.Prompt }],
        };
        if (request.Instruction.Length > 0)
        {
            parameters = parameters with { System = request.Instruction };
        }

        var inputTokens = 0;
        var outputTokens = 0;
        await using var events = client.Messages.CreateStreaming(parameters, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            RawMessageStreamEvent streamEvent;
            try
            {
                if (!await events.MoveNextAsync())
                {
                    break;
                }

                streamEvent = events.Current;
            }
            catch (Exception e) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The draft was stopped.", e, cancellationToken);
            }
            catch (Exception e) when (e is AnthropicException)
            {
                throw Failure(e);
            }

            if (streamEvent.TryPickStart(out var start))
            {
                var usage = start.Message.Usage;
                inputTokens = Input(usage.InputTokens, usage.CacheCreationInputTokens, usage.CacheReadInputTokens);
            }
            else if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
            {
                if (text.Text.Length > 0)
                {
                    yield return new ProseChunk(text.Text);
                }
            }
            else if (streamEvent.TryPickDelta(out var messageDelta))
            {
                // Usage on message_delta is cumulative; input tokens are repeated there on some responses.
                var usage = messageDelta.Usage;
                outputTokens = (int)usage.OutputTokens;
                inputTokens = usage.InputTokens is { } input ? Input(input, usage.CacheCreationInputTokens, usage.CacheReadInputTokens) : inputTokens;
                if (messageDelta.Delta.StopReason == StopReason.Refusal)
                {
                    throw ApiException.Blocked("Anthropic declined to write this passage. Reword the beat and try again.");
                }

                if (messageDelta.Delta.StopReason == StopReason.MaxTokens)
                {
                    throw Unavailable("The draft hit the length limit before it finished. Lower the target length and try again.");
                }
            }
        }

        yield return ProseChunk.Done(new ProseResult(request.Model, inputTokens, outputTokens));
    }

    // Billed input is the uncached tokens plus whatever was written to or read from the prompt cache.
    private static int Input(long input, long? cacheCreation, long? cacheRead) => (int)(input + (cacheCreation ?? 0) + (cacheRead ?? 0));

    // Fixed wording per cause: nothing from the provider's body (which may echo the request) reaches the author.
    private ApiException Failure(Exception e)
    {
        var status = (e as AnthropicApiException)?.StatusCode;
        var type = (e as AnthropicServiceException)?.ErrorType;
        logger.LogWarning(
            e, "Anthropic request failed: HTTP {Status}, error type {ErrorType}, request {RequestId}",
            status == null ? null : (int)status, type?.ToString(), RequestId(e));

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || type is ErrorType.AuthenticationError or ErrorType.PermissionError)
        {
            return ApiException.NotConfigured("Anthropic rejected the API key. Check it in Settings → AI.");
        }

        if ((int?)status == PaymentRequired || type == ErrorType.BillingError)
        {
            return ApiException.NotConfigured("Anthropic reports a billing problem on this account. Check the plan and credits in the Anthropic console.");
        }

        if ((int?)status == Overloaded || type == ErrorType.OverloadedError)
        {
            return Unavailable("Anthropic is overloaded right now. Try the draft again in a moment.");
        }

        if (status == HttpStatusCode.TooManyRequests || type == ErrorType.RateLimitError)
        {
            return Unavailable("Anthropic is rate limiting requests. Try the draft again in a moment.");
        }

        return status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity
            || type is ErrorType.InvalidRequestError or ErrorType.NotFoundError
            ? Unavailable("Anthropic could not use this request. Check the model and the beat, then try again.")
            : Unavailable("Anthropic could not be reached. Try the draft again in a moment.");
    }

    // The request id is in the error body (`request_id`); a mid-stream error event has no body.
    private static string? RequestId(Exception e)
    {
        if (e is not AnthropicApiException { ResponseBody: { Length: > 0 } body })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("request_id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // TODO: a dedicated `provider_unavailable` error code (contracts are frozen this wave) replaces internal_error here.
    private static ApiException Unavailable(string message) => new(ApiErrorCodes.Internal, message);
}
