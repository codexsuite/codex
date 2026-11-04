using System.Net;
using Codex.Application.Features.Ai;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Codex.UnitTest.Contracts;

[TestFixture]
public class AiProvidersTests
{
    private static readonly Uri Base = new("https://api.example.test/v1");
    private static readonly ChatCompletionsLimits Quick = new(TimeSpan.Zero, TimeSpan.FromSeconds(30), 4 * 1024 * 1024);

    [TestCase(AiIntegrationKinds.OpenAiCompatible, typeof(ChatCompletionsProseGenerator))]
    [TestCase(AiIntegrationKinds.OpenRouter, typeof(ChatCompletionsProseGenerator))]
    [TestCase(AiIntegrationKinds.Anthropic, typeof(AnthropicProseGenerator))]
    public void Generator_WhenGivenAKind_ShouldPickItsImplementation(string kind, Type expected)
    {
        var providers = Providers(ChatCompletionsStubHandler.NetworkDown());

        providers.Generator(new AiConnection(kind, Base, "k")).ShouldBeOfType(expected);
    }

    [Test]
    public void Generator_WhenTheKindIsUnknown_ShouldFailAsValidation()
    {
        var failure = Should.Throw<ApiException>(() => Providers(ChatCompletionsStubHandler.NetworkDown()).Generator(new AiConnection("openai_oauth", null, "k")));

        failure.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task ListModelsAsync_WhenTheProviderIsOpenAiCompatible_ShouldReturnSortedDistinctIds()
    {
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.OK, "models.json");
        var guard = new RecordingEndpointGuard();

        var models = await Providers(handler, guard).ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenAiCompatible, Base, "k-1"), CancellationToken.None);

        models.ShouldBe(["gpt-test", "gpt-test-mini"]);
        handler.Last.Method.ShouldBe("GET");
        handler.Last.Url.ShouldBe("https://api.example.test/v1/models");
        handler.Last.Headers["Authorization"].ShouldBe("Bearer k-1");
        guard.Checked.ShouldBe([Base]);
    }

    [Test]
    public async Task ListModelsAsync_WhenTheProviderIsOpenRouter_ShouldAskItsFixedBaseWithItsHeaders()
    {
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.OK, "models.json");

        await Providers(handler).ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenRouter, null, "k"), CancellationToken.None);

        handler.Last.Url.ShouldBe("https://openrouter.ai/api/v1/models");
        handler.Last.Headers["X-Title"].ShouldBe("Codex");
        handler.ClientNames.ShouldBe([AiHttpClients.OpenRouter]);
    }

    [Test]
    public async Task ListModelsAsync_WhenTheProviderIsAnthropic_ShouldUseTheKeyHeaderAndVersionOnTheAnthropicClient()
    {
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.OK, "anthropic-models.json");

        var models = await Providers(handler).ListModelsAsync(new AiConnection(AiIntegrationKinds.Anthropic, null, "sk-ant-k"), CancellationToken.None);

        models.ShouldBe(["claude-test-a", "claude-test-b"]);
        handler.Last.Url.ShouldStartWith("https://api.anthropic.com/v1/models");
        handler.Last.Headers["x-api-key"].ShouldBe("sk-ant-k");
        handler.Last.Headers["anthropic-version"].ShouldBe("2023-06-01");
        handler.Last.Headers.ShouldNotContainKey("Authorization");
        handler.ClientNames.ShouldBe([AnthropicProseGenerator.HttpClientName]);
    }

    [Test]
    public async Task ListModelsAsync_WhenTheAnswerHasNoModelList_ShouldReturnNothing()
    {
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.OK, "chat-500.json");

        var models = await Providers(handler).ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenRouter, null, "k"), CancellationToken.None);

        models.ShouldBeEmpty();
    }

    [Test]
    public async Task ListModelsAsync_WhenTheKeyIsRejected_ShouldFailAsNotConfiguredWithoutTheKey()
    {
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.Unauthorized, "chat-401.json");

        var failure = await Should.ThrowAsync<ApiException>(
            async () => await Providers(handler).ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenAiCompatible, Base, "sk-secret-1"), CancellationToken.None));

        failure.Code.ShouldBe(ApiErrorCodes.NotConfigured);
        failure.Message.ShouldNotContain("sk-secret-1");
    }

    [Test]
    public async Task ListModelsAsync_WhenTheGuardRefusesTheBaseUrl_ShouldFailAsValidationAndSendNothing()
    {
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.OK, "models.json");

        var failure = await Should.ThrowAsync<ApiException>(
            async () => await Providers(handler, new RecordingEndpointGuard(allow: false))
                .ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenAiCompatible, Base, "k"), CancellationToken.None));

        failure.Code.ShouldBe(ApiErrorCodes.Validation);
        handler.RequestCount.ShouldBe(0);
    }

    [Test]
    public async Task ListModelsAsync_WhenTheAnswerIsTooLarge_ShouldFail()
    {
        var handler = ChatCompletionsStubHandler.Json(HttpStatusCode.OK, "models.json");
        var providers = new AiProviders(handler, new RecordingEndpointGuard(), NullLoggerFactory.Instance, Quick with { MaxResponseBytes = 20 });

        var failure = await Should.ThrowAsync<ApiException>(
            async () => await providers.ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenRouter, null, "k"), CancellationToken.None));

        failure.Message.ShouldContain("more than this app accepts");
    }

    [Test]
    public async Task ListModelsAsync_WhenTheBodyNeverFinishes_ShouldFailAsUnreachableAtTheLimit()
    {
        var providers = new AiProviders(
            ChatCompletionsStubHandler.HangingBody(HttpStatusCode.OK), new RecordingEndpointGuard(), NullLoggerFactory.Instance, Quick with { IdleTimeout = TimeSpan.FromMilliseconds(100) });

        var failure = await Should.ThrowAsync<ApiException>(
            async () => await providers.ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenRouter, null, "k"), CancellationToken.None));

        failure.Message.ShouldContain("could not be reached");
    }

    [Test]
    public async Task ListModelsAsync_WhenTheCallerCancelsWhileReading_ShouldStillThrowCancellation()
    {
        using var cancel = new CancellationTokenSource(100);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Providers(ChatCompletionsStubHandler.HangingBody(HttpStatusCode.OK))
                .ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenRouter, null, "k"), cancel.Token));
    }

    [TestCase("\"x\"")]
    [TestCase("[1]")]
    [TestCase("null")]
    [TestCase("{\"data\":\"x\"}")]
    [TestCase("{\"data\":[null,1,{\"id\":7},{\"id\":\"ok\"}]}")]
    public async Task ListModelsAsync_WhenTheShapeIsOdd_ShouldNeverThrowAnythingButApiException(string body)
    {
        var handler = ChatCompletionsStubHandler.Text(body);
        var providers = Providers(handler);

        try
        {
            var models = await providers.ListModelsAsync(new AiConnection(AiIntegrationKinds.OpenRouter, null, "k"), CancellationToken.None);
            models.ShouldBeSubsetOf(["ok"]);
        }
        catch (ApiException failure)
        {
            failure.Code.ShouldBe(ApiErrorCodes.Internal);
        }
    }

    [Test]
    public void CreateHandler_WhenBuilt_ShouldNotFollowRedirectsUseAProxyOrConnectWithoutTheAddressCheck()
    {
        foreach (var local in new[] { false, true })
        {
            using var handler = AiProviderServices.CreateHandler(local);

            handler.AllowAutoRedirect.ShouldBeFalse();
            handler.UseProxy.ShouldBeFalse();
            handler.ConnectCallback.ShouldNotBeNull();
        }
    }

    [Test]
    public async Task CreateHandler_WhenTheHostIsALoopbackLiteralAndLocalIsOff_ShouldRefuseToConnect()
    {
        using var handler = AiProviderServices.CreateHandler(localAllowed: false);
        using var client = new HttpClient(handler);

        await Should.ThrowAsync<HttpRequestException>(async () => await client.GetAsync("http://127.0.0.1:9/"));
    }

    [Test]
    public async Task CreateHandler_WhenTheHostIsALinkLocalLiteralEvenWithLocalOn_ShouldRefuseToConnect()
    {
        using var handler = AiProviderServices.CreateHandler(localAllowed: true);
        using var client = new HttpClient(handler);

        await Should.ThrowAsync<HttpRequestException>(async () => await client.GetAsync("http://169.254.169.254/"));
    }

    private static AiProviders Providers(IHttpClientFactory clients, RecordingEndpointGuard? guard = null)
        => new(clients, guard ?? new RecordingEndpointGuard(), NullLoggerFactory.Instance, Quick);
}
