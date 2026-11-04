using Codex.Application.Features.Ai;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fakes;

namespace Codex.UnitTest.Ai;

// IntegrationProseGenerator over a stub resolver and fake providers: the not_configured refusal, the pass-through,
// and what it records on the integration (use, error, nothing on Stop).
[TestFixture]
public class IntegrationProseGeneratorTests
{
    private static readonly Guid IntegrationId = Guid.NewGuid();

    private StubResolver _resolver = null!;

    [SetUp]
    public void CreateResolver() => _resolver = new StubResolver();

    [Test]
    public void GenerateAsync_WhenNoIntegrationIsNamed_ShouldReturnNotConfiguredWithTheConnectMessage()
    {
        var generator = new IntegrationProseGenerator(_resolver, new RecordingAiProviders());

        var failure = Should.Throw<ApiException>(async () => await Drain(generator, new ProseRequest("m", "", "A beat.")));

        failure.Code.ShouldBe(ApiErrorCodes.NotConfigured);
        failure.Message.ShouldBe("Connect an AI provider in Settings → AI to write drafts.");
    }

    [Test]
    public async Task GenerateAsync_WhenTheStreamFinishes_ShouldPassChunksThroughAndRecordUseWithoutAnError()
    {
        var providers = new RecordingAiProviders(new FakeProseGenerator(["One. ", "Two."]));
        var generator = new IntegrationProseGenerator(_resolver, providers);

        var chunks = await Drain(generator, new ProseRequest("m", "", "A beat.", IntegrationId));

        chunks.Where(c => c.Final == null).Select(c => c.Text).ShouldBe(["One. ", "Two."]);
        providers.Connections.ShouldHaveSingleItem().ApiKey.ShouldBe("sk-secret-1234");
        _resolver.Uses.ShouldBe([(IntegrationId, (string?)null)]);
    }

    [Test]
    public async Task GenerateAsync_WhenTheProviderFailsWithAnApiException_ShouldRecordItsMessageAndRethrow()
    {
        var providers = new RecordingAiProviders(new FakeProseGenerator(["One. "], null, ApiException.NotConfigured("The key was rejected.")));
        var generator = new IntegrationProseGenerator(_resolver, providers);

        var failure = await Should.ThrowAsync<ApiException>(async () => await Drain(generator, new ProseRequest("m", "", "A beat.", IntegrationId)));

        failure.Message.ShouldBe("The key was rejected.");
        _resolver.Uses.ShouldBe([(IntegrationId, (string?)"The key was rejected.")]);
    }

    [Test]
    public async Task GenerateAsync_WhenTheCallerStops_ShouldRecordNothing()
    {
        var gate = new TaskCompletionSource().Task;
        var generator = new IntegrationProseGenerator(_resolver, new RecordingAiProviders(new FakeProseGenerator(["One. ", "Two."], gate)));
        using var stop = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in generator.GenerateAsync(new ProseRequest("m", "", "A beat.", IntegrationId), stop.Token))
            {
                chunk.Text.ShouldBe("One. ");
                await stop.CancelAsync();
            }
        });

        _resolver.Uses.ShouldBeEmpty();
    }

    private static async Task<List<ProseChunk>> Drain(IProseGenerator generator, ProseRequest request)
    {
        var chunks = new List<ProseChunk>();
        await foreach (var chunk in generator.GenerateAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class StubResolver : IAiIntegrationResolver
    {
        private readonly List<(Guid Id, string? Error)> _uses = [];

        public IReadOnlyList<(Guid Id, string? Error)> Uses => _uses;

        public Task<ResolvedAiIntegration?> ResolveAsync(Guid? integrationId, CancellationToken cancellationToken)
            => Task.FromResult<ResolvedAiIntegration?>(new ResolvedAiIntegration(
                IntegrationId, "anthropic", "Mine", "m", new AiConnection("anthropic", null, "sk-secret-1234")));

        public Task RecordUseAsync(Guid integrationId, string? error, CancellationToken cancellationToken)
        {
            _uses.Add((integrationId, error));
            return Task.CompletedTask;
        }
    }
}
