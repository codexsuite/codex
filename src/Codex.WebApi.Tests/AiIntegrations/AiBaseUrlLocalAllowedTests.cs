using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Network;
using Codex.Contracts.Ai.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.AiIntegrations;

// SSRF with the server owner's CODEX_AI_ALLOW_LOCAL_ENDPOINTS=true: http:// and private addresses pass (Ollama, LM
// Studio); credentials, a query or a fragment in the URL are still refused. The real guard, configured in memory.
[TestFixture]
public class AiBaseUrlLocalAllowedTests : AiIntegrationFixture
{
    protected override void ConfigureMore(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AiSettings.AllowLocalEndpointsKey] = "TRUE" })
            .Build();
        services.AddSingleton<IAiEndpointGuard>(sp => new AiEndpointGuard(configuration, sp.GetRequiredService<IHostResolver>()));
    }

    [TestCase("http://localhost:11434/v1", "http://localhost:11434/v1")]
    [TestCase("https://10.1.2.3/v1/", "https://10.1.2.3/v1")]
    [TestCase("http://192.168.1.5:1234/v1", "http://192.168.1.5:1234/v1")]
    [TestCase("https://intranet.test/v1", "https://intranet.test/v1")]
    public async Task BaseUrl_WhenLocalEndpointsAreAllowed_ShouldBeAccepted(string baseUrl, string stored)
    {
        using var create = await Client.PostAsJsonAsync("/api/ai/integrations", OpenAi(baseUrl: baseUrl));
        using var test = await Client.PostAsJsonAsync("/api/ai/integrations/test", new TestAiConnectionRequest("openai_compatible", baseUrl, "sk-test-1234567890"));

        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await create.Content.ReadFromJsonAsync<AiIntegrationDto>())!.BaseUrl.ShouldBe(stored);
        test.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestCase("http://user:secret@localhost:11434/v1")]
    [TestCase("http://localhost:11434/v1?x=1")]
    [TestCase("ftp://localhost/v1")]
    public async Task BaseUrl_WhenItHasCredentialsAQueryOrAnotherScheme_ShouldStillBeRefused(string baseUrl)
    {
        using var response = await Client.PostAsJsonAsync("/api/ai/integrations", OpenAi(baseUrl: baseUrl));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task List_ShouldReportThatLocalEndpointsAreAllowed()
    {
        (await ListAsync(Client)).LocalEndpointsAllowed.ShouldBeTrue();
    }
}
