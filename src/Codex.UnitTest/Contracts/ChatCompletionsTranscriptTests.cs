using System.Net;
using System.Text.Json;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.UnitTest.Contracts;

[TestFixture]
public class ChatCompletionsTranscriptTests
{
    private const string Key = "sk-test-key-9f3a";
    private static readonly string[] Transcript = ["The ", "door ", "creaked."];
    private static readonly ProseRequest Request = new("gpt-test", "Write plainly.", "The door opened slowly.");
    private static readonly Uri Base = new("https://api.example.test/v1/");
    private static readonly ChatCompletionsLimits Quick = new(TimeSpan.Zero, TimeSpan.FromSeconds(30), 4 * 1024 * 1024);

    [Test]
    public async Task GenerateAsync_WhenTheStreamCompletes_ShouldYieldChunksInOrderAndTheUsageTokensLast()
    {
        var chunks = await Collect(Generator(ChatCompletionsStubHandler.Streaming(Transcript)));

        chunks.Take(3).Select(c => c.Text).ShouldBe(Transcript);
        chunks.Count.ShouldBe(4);
        var final = chunks[^1].Final.ShouldNotBeNull();
        final.InputTokens.ShouldBe(25);
        final.OutputTokens.ShouldBe(6);
        final.Model.ShouldBe(Request.Model);
    }

    [Test]
    public async Task GenerateAsync_WhenCalled_ShouldPostTheStreamingRequestToChatCompletionsWithABearerKey()
    {
        var handler = ChatCompletionsStubHandler.Streaming(Transcript);

        await Collect(Generator(handler));

        var sent = handler.Last;
        sent.Method.ShouldBe("POST");
        sent.Url.ShouldBe("https://api.example.test/v1/chat/completions");
        sent.Headers["Authorization"].ShouldBe($"Bearer {Key}");
        sent.Headers.ShouldNotContainKey("HTTP-Referer");
        handler.ClientNames.ShouldBe([AiHttpClients.OpenAiCompatible]);
        using var body = JsonDocument.Parse(sent.Body.ShouldNotBeNull());
        var root = body.RootElement;
        root.GetProperty("model").GetString().ShouldBe("gpt-test");
        root.GetProperty("stream").GetBoolean().ShouldBeTrue();
        root.GetProperty("stream_options").GetProperty("include_usage").GetBoolean().ShouldBeTrue();
        root.TryGetProperty("max_tokens", out _).ShouldBeFalse();
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("role").GetString()).ShouldBe(["system", "user"]);
        messages[0].GetProperty("content").GetString().ShouldBe("Write plainly.");
        messages[1].GetProperty("content").GetString().ShouldBe("The door opened slowly.");
        sent.Body.ShouldNotContain(Key);
    }

    [Test]
    public async Task GenerateAsync_WhenThereIsNoInstruction_ShouldSendOnlyTheUserMessage()
    {
        var handler = ChatCompletionsStubHandler.Streaming(Transcript);

        await Collect(Generator(handler), Request with { Instruction = string.Empty });

        handler.Last.Body.ShouldNotBeNull().ShouldNotContain("\"system\"");
    }

    [Test]
    public async Task GenerateAsync_WhenTheBaseUrlHasNoTrailingSlash_ShouldStillPostToChatCompletions()
    {
        var handler = ChatCompletionsStubHandler.Streaming(Transcript);
        var generator = new ChatCompletionsProseGenerator(
            handler, new RecordingEndpointGuard(), new AiConnection(AiIntegrationKinds.OpenAiCompatible, new Uri("https://api.example.test/v1"), Key),
            NullLogger<ChatCompletionsProseGenerator>.Instance, Quick);

        await Collect(generator);

        handler.Last.Url.ShouldBe("https://api.example.test/v1/chat/completions");
    }

    [Test]
    public async Task GenerateAsync_WhenTheKeyIsEmpty_ShouldSendNoAuthorizationHeader()
    {
        var handler = ChatCompletionsStubHandler.Streaming(Transcript);

        await Collect(Generator(handler, key: string.Empty));

        handler.Last.Headers.ShouldNotContainKey("Authorization");
    }

    [Test]
    public async Task GenerateAsync_WhenTheProviderIsOpenRouter_ShouldUseItsFixedBaseHeadersAndNoGuard()
    {
        var handler = ChatCompletionsStubHandler.Streaming(Transcript, fixture: "chat-stream-openrouter.sse");
        var guard = new RecordingEndpointGuard(allow: false);
        var generator = new ChatCompletionsProseGenerator(
            handler, guard, new AiConnection(AiIntegrationKinds.OpenRouter, null, Key), NullLogger<ChatCompletionsProseGenerator>.Instance, Quick);

        var chunks = await Collect(generator);

        handler.Last.Url.ShouldBe("https://openrouter.ai/api/v1/chat/completions");
        handler.Last.Headers["HTTP-Referer"].ShouldBe("https://github.com/codexsuite/codex");
        handler.Last.Headers["X-Title"].ShouldBe("Codex");
        handler.Last.Headers["Authorization"].ShouldBe($"Bearer {Key}");
        handler.ClientNames.ShouldBe([AiHttpClients.OpenRouter]);
        guard.Checked.ShouldBeEmpty();
        chunks[^1].Final.ShouldNotBeNull().InputTokens.ShouldBe(25);
    }

    [Test]
    public async Task GenerateAsync_WhenTheProviderIsOpenAiCompatible_ShouldAskTheGuardBeforeEveryRequest()
    {
        var guard = new RecordingEndpointGuard();
        var handler = ChatCompletionsStubHandler.Sequence(
            ChatCompletionsStubHandler.Json(HttpStatusCode.InternalServerError, "chat-500.json"), ChatCompletionsStubHandler.Streaming(Transcript));

        await Collect(Generator(handler, guard: guard));

        guard.Checked.ShouldBe([Base, Base]);
    }

    [Test]
    public async Task GenerateAsync_WhenTheGuardRefusesTheBaseUrl_ShouldFailAsValidationAndSendNothing()
    {
        var handler = ChatCompletionsStubHandler.Streaming(Transcript);

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(handler, guard: new RecordingEndpointGuard(allow: false))));

        failure.Code.ShouldBe(ApiErrorCodes.Validation);
        handler.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task GenerateAsync_WhenTheProviderRedirects_ShouldNotFollowAndFailAsUnusable()
    {
        var handler = ChatCompletionsStubHandler.Redirect("http://169.254.169.254/latest");

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(handler)));

        handler.RequestCount.ShouldBe(1);
        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldContain("could not use this request");
    }

    [TestCase(HttpStatusCode.Unauthorized, "chat-401.json", ApiErrorCodes.NotConfigured, "rejected the API key. Check it in Settings → AI.", 1)]
    [TestCase(HttpStatusCode.Forbidden, "chat-401.json", ApiErrorCodes.NotConfigured, "rejected the API key", 1)]
    [TestCase(HttpStatusCode.PaymentRequired, "chat-402.json", ApiErrorCodes.NotConfigured, "no credit left", 1)]
    [TestCase(HttpStatusCode.TooManyRequests, "chat-429-quota.json", ApiErrorCodes.NotConfigured, "no credit left", 1)]
    [TestCase(HttpStatusCode.TooManyRequests, "chat-429.json", ApiErrorCodes.Internal, "busy", 3)]
    [TestCase(HttpStatusCode.InternalServerError, "chat-500.json", ApiErrorCodes.Internal, "busy", 3)]
    [TestCase(HttpStatusCode.NotFound, "chat-404.json", ApiErrorCodes.Internal, "could not use this request. Check the model", 1)]
    [TestCase(HttpStatusCode.BadRequest, "chat-404.json", ApiErrorCodes.Internal, "could not use this request", 1)]
    [TestCase(HttpStatusCode.UnprocessableEntity, "chat-404.json", ApiErrorCodes.Internal, "could not use this request", 1)]
    public async Task GenerateAsync_WhenTheApiAnswersWithAnError_ShouldFailWithAFixedMessageNoBodyAndNoKey(
        HttpStatusCode status, string fixture, string code, string wording, int requests)
    {
        var handler = ChatCompletionsStubHandler.Json(status, fixture);

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(handler)));

        failure.Code.ShouldBe(code);
        failure.Message.ShouldContain(wording);
        failure.Message.ShouldNotContain(Key);
        failure.Message.ShouldNotContain("sk-proj-leak");
        failure.Message.ShouldNotContain("platform.openai.com");
        handler.RequestCount.ShouldBe(requests);
    }

    [Test]
    public async Task GenerateAsync_WhenTheNetworkIsDown_ShouldRetryTwiceThenFailAsUnreachable()
    {
        var handler = ChatCompletionsStubHandler.NetworkDown();

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(handler)));

        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldContain("could not be reached");
        handler.RequestCount.ShouldBe(3);
    }

    [Test]
    public async Task GenerateAsync_WhenTheFirstAnswerIsRateLimited_ShouldRetryBeforeAnyTextAndStream()
    {
        var handler = ChatCompletionsStubHandler.Sequence(
            ChatCompletionsStubHandler.Json(HttpStatusCode.TooManyRequests, "chat-429.json"), ChatCompletionsStubHandler.Streaming(Transcript));

        var chunks = await Collect(Generator(handler));

        handler.RequestCount.ShouldBe(2);
        string.Concat(chunks.Select(c => c.Text)).ShouldBe("The door creaked.");
    }

    [Test]
    public async Task GenerateAsync_WhenTheStreamFailsAfterText_ShouldNotRetryAndKeepTheText()
    {
        var handler = ChatCompletionsStubHandler.Sequence(
            ChatCompletionsStubHandler.Streaming(["The "], failWith: new IOException("connection reset")), ChatCompletionsStubHandler.Streaming(Transcript));
        var received = new List<string>();

        var failure = await Should.ThrowAsync<ApiException>(async () =>
        {
            await foreach (var chunk in Generator(handler).GenerateAsync(Request, CancellationToken.None))
            {
                received.Add(chunk.Text);
            }
        });

        received.ShouldBe(["The "]);
        failure.Message.ShouldContain("could not be reached");
        handler.RequestCount.ShouldBe(1);
    }

    [Test]
    public async Task GenerateAsync_WhenTheDraftHitsTheLengthLimit_ShouldYieldTheTextThenFail()
    {
        var received = new List<string>();

        var failure = await Should.ThrowAsync<ApiException>(async () =>
        {
            await foreach (var chunk in Generator(ChatCompletionsStubHandler.StreamingFile("chat-length.sse")).GenerateAsync(Request, CancellationToken.None))
            {
                received.Add(chunk.Text);
            }
        });

        received.ShouldBe(["The door "]);
        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldContain("length limit");
    }

    [Test]
    public async Task GenerateAsync_WhenTheContentIsFiltered_ShouldFailAsBlocked()
    {
        var failure = await Should.ThrowAsync<ApiException>(
            async () => await Collect(Generator(ChatCompletionsStubHandler.StreamingFile("chat-content-filter.sse"))));

        failure.Code.ShouldBe(ApiErrorCodes.Blocked);
    }

    [Test]
    public async Task GenerateAsync_WhenTheStreamCarriesAnErrorObject_ShouldYieldTheTextThenFailWithoutTheProvidersMessage()
    {
        var received = new List<string>();

        var failure = await Should.ThrowAsync<ApiException>(async () =>
        {
            await foreach (var chunk in Generator(ChatCompletionsStubHandler.StreamingFile("chat-error-midstream.sse")).GenerateAsync(Request, CancellationToken.None))
            {
                received.Add(chunk.Text);
            }
        });

        received.ShouldBe(["The "]);
        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldNotContain("sk-leak-in-body");
    }

    [Test]
    public async Task GenerateAsync_WhenTheStreamGoesSilent_ShouldFailAtTheIdleLimit()
    {
        var silent = ChatCompletionsStubHandler.Silent("data: {\"choices\":[{\"delta\":{\"content\":\"The \"}}]}\n\n");
        var limits = Quick with { IdleTimeout = TimeSpan.FromMilliseconds(200) };

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(silent, limits: limits)));

        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldContain("stopped answering");
    }

    [Test]
    public async Task GenerateAsync_WhenTheStreamEndsWithoutAFinishOrDone_ShouldFailInsteadOfCompleting()
    {
        var failure = await Should.ThrowAsync<ApiException>(
            async () => await Collect(Generator(ChatCompletionsStubHandler.Streaming(["The "], failWith: new IOException("cut")))));

        failure.Message.ShouldContain("could not be reached");
    }

    [Test]
    public async Task GenerateAsync_WhenTheResponseIsTooLarge_ShouldFail()
    {
        var limits = Quick with { MaxResponseBytes = 1000 };

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(ChatCompletionsStubHandler.Oversized(5000), limits: limits)));

        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldContain("more than this app accepts");
    }

    [Test]
    public async Task GenerateAsync_WhenALineNeverEnds_ShouldFailAtTheSizeCapInsteadOfBuffering()
    {
        var limits = Quick with { MaxResponseBytes = 1000 };

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(ChatCompletionsStubHandler.Unterminated(1_000_000), limits: limits)));

        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldContain("more than this app accepts");
    }

    [Test]
    public async Task GenerateAsync_WhenAnErrorBodyNeverFinishes_ShouldFailAsUnreachableAtTheLimit()
    {
        var handler = ChatCompletionsStubHandler.HangingBody(HttpStatusCode.InternalServerError);
        var limits = Quick with { IdleTimeout = TimeSpan.FromMilliseconds(100) };

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(handler, limits: limits)));

        failure.Code.ShouldBe(ApiErrorCodes.Internal);
        failure.Message.ShouldContain("could not be reached");
    }

    [TestCase("data: \"keep-alive\"\n\n")]
    [TestCase("data: 42\n\n")]
    [TestCase("data: null\n\n")]
    [TestCase("data: [1,2]\n\n")]
    [TestCase("data:\n\n")]
    [TestCase("data: {\"choices\":[null]}\n\n")]
    [TestCase("data: {\"choices\":[\"x\"]}\n\n")]
    [TestCase("data: {\"choices\":\"x\"}\n\n")]
    [TestCase("data: {\"choices\":[{\"delta\":\"x\"}]}\n\n")]
    [TestCase("data: {\"usage\":\"x\"}\n\n")]
    public async Task GenerateAsync_WhenAFrameHasAnOddShape_ShouldSkipItAndStillFinish(string odd)
    {
        var handler = ChatCompletionsStubHandler.Text(odd + "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":\"stop\"}]}\n\n");

        var chunks = await Collect(Generator(handler));

        chunks.Select(c => c.Text).Where(t => t.Length > 0).ShouldBe(["Hi"]);
        chunks[^1].Final.ShouldNotBeNull();
    }

    [Test]
    public async Task GenerateAsync_WhenAFrameIsNotJson_ShouldFailAsUnreadable()
    {
        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(ChatCompletionsStubHandler.Text("data: {oops\n\n"))));

        failure.Message.ShouldContain("could not read");
    }

    [Test]
    public async Task GenerateAsync_WhenTheApiFails_ShouldLogStatusAndRequestIdButNeitherTheKeyNorTheBody()
    {
        var logger = new CapturingLogger<ChatCompletionsProseGenerator>();
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.Unauthorized, "chat-401.json", requestId: "req-abc-123");

        await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(handler, logger: logger)));

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("401");
        entry.Message.ShouldContain("req-abc-123");
        Everything(logger).ShouldNotContain(Key);
        Everything(logger).ShouldNotContain("sk-proj-leak");
    }

    [Test]
    public async Task GenerateAsync_WhenAnythingFailsOrSucceeds_ShouldNeverPutTheKeyInAnExceptionOrLogLine()
    {
        var logger = new CapturingLogger<ChatCompletionsProseGenerator>();
        var handlers = new[]
        {
            ChatCompletionsStubHandler.NetworkDown(),
            ChatCompletionsStubHandler.Json(HttpStatusCode.PaymentRequired, "chat-402.json"),
            ChatCompletionsStubHandler.Json(HttpStatusCode.InternalServerError, "chat-500.json"),
            ChatCompletionsStubHandler.StreamingFile("chat-error-midstream.sse"),
            ChatCompletionsStubHandler.Redirect("https://elsewhere.test/"),
        };

        foreach (var handler in handlers)
        {
            var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(Generator(handler, logger: logger)));
            (failure + failure.Message).ShouldNotContain(Key);
        }

        logger.Entries.ShouldNotBeEmpty();
        Everything(logger).ShouldNotContain(Key);
    }

    private static string Everything(CapturingLogger<ChatCompletionsProseGenerator> logger) =>
        string.Join("\n", logger.Entries.Select(e => e.Message + e.Exception));

    private static ChatCompletionsProseGenerator Generator(
        IHttpClientFactory clients, RecordingEndpointGuard? guard = null, string key = Key, ChatCompletionsLimits? limits = null, ILogger<ChatCompletionsProseGenerator>? logger = null)
        => new(clients, guard ?? new RecordingEndpointGuard(), new AiConnection(AiIntegrationKinds.OpenAiCompatible, Base, key), logger ?? NullLogger<ChatCompletionsProseGenerator>.Instance, limits ?? Quick);

    private static async Task<List<ProseChunk>> Collect(IProseGenerator generator, ProseRequest? request = null)
    {
        var chunks = new List<ProseChunk>();
        await foreach (var chunk in generator.GenerateAsync(request ?? Request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }
}
