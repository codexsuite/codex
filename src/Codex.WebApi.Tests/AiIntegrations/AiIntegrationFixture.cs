using System.Net;
using System.Net.Http.Json;
using Codex.Application.Network;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts.Ai.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codex.WebApi.Tests.AiIntegrations;

// Base for the AI integration scenarios (#36): the bare schema with JD, a fake provider, a fixed host resolver (the
// hosts in PrivateHosts resolve to private addresses, every other host to a public one) and captured logs.
public abstract class AiIntegrationFixture : SchemaApiFixture
{
    public const string OpenAiUrl = "https://api.example.test/v1";

    protected FakeAiProviders Providers { get; } = new();

    protected CapturedLogs Logs { get; } = new();

    protected virtual Dictionary<string, string> PrivateHosts => new() { ["intranet.test"] = "10.0.0.5" };

    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
    {
        services.AddSingleton<IAiProviders>(Providers);
        services.AddSingleton<IHostResolver>(new FixedHostResolver(PrivateHosts));
        services.AddSingleton<ILoggerProvider>(Logs);
        ConfigureMore(services);
    };

    protected virtual void ConfigureMore(IServiceCollection services)
    {
        // Scenarios that swap one more double override this.
    }

    protected static CreateAiIntegrationRequest OpenAi(string key = "sk-proj-AAAA1111BBBB2222", string baseUrl = OpenAiUrl) =>
        new(AiIntegrationKinds.OpenAiCompatible, "My gateway", baseUrl, key, null);

    protected static CreateAiIntegrationRequest OpenRouter(string key = "sk-or-v1-zzzz9999yyyy") =>
        new(AiIntegrationKinds.OpenRouter, "OpenRouter", null, key, null);

    protected static async Task<AiIntegrationDto> CreateAsync(HttpClient client, CreateAiIntegrationRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/ai/integrations", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AiIntegrationDto>())!;
    }

    protected static async Task<AiSettingsDto> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AiSettingsDto>("/api/ai/integrations"))!;
}
