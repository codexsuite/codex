using System.Net;
using Codex.Application.Features.Ai;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.UnitTest.Contracts;

[TestFixture]
public class AnthropicProseGeneratorTranscriptTests
{
    private const string Key = "sk-ant-test-key";
    private static readonly string[] Transcript = ["The ", "door ", "creaked."];
    private static readonly ProseRequest Request = new("claude-opus-5-5", "Write plainly.", "The door opened slowly.");

    [Test]
    public async Task GenerateAsync_WhenTheTranscriptStreamCompletes_ShouldReportTheTokenCountsFromTheStream()
    {
        var generator = Generator(AnthropicStubHandler.Streaming(Transcript), Key);

        var chunks = await Collect(generator);

        var final = chunks[^1].Final.ShouldNotBeNull();
        final.InputTokens.ShouldBe(25);
        final.OutputTokens.ShouldBe(6);
        final.Model.ShouldBe(Request.Model);
        string.Concat(chunks.Select(c => c.Text)).ShouldBe("The door creaked.");
    }

    [Test]
    public async Task GenerateAsync_WhenCalled_ShouldPostTheStreamingRequestWithTheKeyHeaderOnly()
    {
        var handler = AnthropicStubHandler.Streaming(Transcript);

        await Collect(Generator(handler, Key));

        handler.RequestUrl.ShouldBe("https://api.anthropic.com/v1/messages");
        handler.ApiKeyHeader.ShouldBe(Key);
        var body = handler.RequestBody.ShouldNotBeNull();
        body.ShouldContain("\"model\":\"claude-opus-5-5\"");
        body.ShouldContain("\"stream\":true");
        body.ShouldContain("Write plainly.");
        body.ShouldContain("The door opened slowly.");
        body.ShouldNotContain(Key);
    }

    [Test]
    public async Task GenerateAsync_WhenThereIsNoInstruction_ShouldSendNoSystemPrompt()
    {
        var handler = AnthropicStubHandler.Streaming(Transcript);

        await Collect(Generator(handler, Key), Request with { Instruction = string.Empty });

        handler.RequestBody.ShouldNotBeNull().ShouldNotContain("\"system\"");
    }

    [TestCase(HttpStatusCode.Unauthorized, "anthropic-401.json", ApiErrorCodes.NotConfigured, "rejected the API key")]
    [TestCase(HttpStatusCode.PaymentRequired, "anthropic-402.json", ApiErrorCodes.NotConfigured, "billing problem")]
    [TestCase(HttpStatusCode.TooManyRequests, "anthropic-429.json", ApiErrorCodes.Internal, "rate limiting")]
    [TestCase((HttpStatusCode)529, "anthropic-529.json", ApiErrorCodes.Internal, "overloaded")]
    public async Task GenerateAsync_WhenTheApiAnswersWithAnError_ShouldFailWithAFixedMessageAndNoKey(
        HttpStatusCode status, string fixture, string code, string wording)
    {
        var generator = Generator(AnthropicStubHandler.Error(status, fixture), Key);

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(generator));

        failure.Code.ShouldBe(code);
        failure.Message.ShouldContain(wording);
        failure.Message.ShouldNotContain(Key);
        failure.Message.ShouldNotContain("req_011C");
    }

    [Test]
    public async Task GenerateAsync_WhenTheStreamIsOverloadedAfterSomeText_ShouldYieldTheTextThenFail()
    {
        var generator = Generator(
            AnthropicStubHandler.Streaming(["The "], interruptedBy: "anthropic-error-overloaded-midstream.sse"), Key);
        var received = new List<string>();

        var failure = await Should.ThrowAsync<ApiException>(async () =>
        {
            await foreach (var chunk in generator.GenerateAsync(Request, CancellationToken.None))
            {
                received.Add(chunk.Text);
            }
        });

        received.ShouldBe(["The "]);
        failure.Message.ShouldContain("overloaded");
    }

    [Test]
    public async Task GenerateAsync_WhenTheModelRefuses_ShouldFailAsBlockedWithoutADraft()
    {
        var generator = Generator(AnthropicStubHandler.StreamingFile("anthropic-refusal.sse"), Key);

        var failure = await Should.ThrowAsync<ApiException>(async () => await Collect(generator));

        failure.Code.ShouldBe(ApiErrorCodes.Blocked);
    }

    [Test]
    public async Task GenerateAsync_WhenTheInputWasCached_ShouldCountCacheWritesAndReadsAsInput()
    {
        var generator = Generator(AnthropicStubHandler.StreamingFile("anthropic-stream-cached.sse"), Key);

        var chunks = await Collect(generator);

        chunks[^1].Final.ShouldNotBeNull().InputTokens.ShouldBe(5 + 300 + 2000);
    }

    [Test]
    public async Task GenerateAsync_WhenTheDraftHitsTheLengthLimit_ShouldYieldTheTextThenFail()
    {
        var generator = Generator(AnthropicStubHandler.StreamingFile("anthropic-max-tokens.sse"), Key);
        var received = new List<string>();

        var failure = await Should.ThrowAsync<ApiException>(async () =>
        {
            await foreach (var chunk in generator.GenerateAsync(Request, CancellationToken.None))
            {
                received.Add(chunk.Text);
            }
        });

        received.ShouldBe(["The door "]);
        failure.Message.ShouldContain("length limit");
    }

    [Test]
    public async Task GenerateAsync_WhenTheFirstAnswerIsOverloaded_ShouldRetryBeforeAnyTextAndStream()
    {
        var handler = AnthropicStubHandler.Sequence(
            AnthropicStubHandler.Error((HttpStatusCode)529, "anthropic-529.json"), AnthropicStubHandler.Streaming(Transcript));

        var chunks = await Collect(Generator(handler, Key));

        handler.RequestCount.ShouldBe(2);
        string.Concat(chunks.Select(c => c.Text)).ShouldBe("The door creaked.");
    }

    [Test]
    public async Task GenerateAsync_WhenTheApiFails_ShouldLogStatusErrorTypeAndRequestIdButNotTheKey()
    {
        var logger = new CapturingLogger<AnthropicProseGenerator>();
        var generator = new AnthropicProseGenerator(AnthropicStubHandler.Error(HttpStatusCode.TooManyRequests, "anthropic-429.json"), Key, logger);

        await Should.ThrowAsync<ApiException>(async () => await Collect(generator));

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("429");
        entry.Message.ShouldContain("RateLimitError");
        entry.Message.ShouldContain("req_011CTestRateLimit");
        entry.Exception.ShouldNotBeNull();
        (entry.Message + entry.Exception).ShouldNotContain(Key);
    }

    private static AnthropicProseGenerator Generator(IHttpClientFactory clients, string key)
        => new(clients, key, NullLogger<AnthropicProseGenerator>.Instance);

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
