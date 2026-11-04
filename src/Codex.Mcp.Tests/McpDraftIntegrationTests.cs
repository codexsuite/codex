using Codex.Application.Features.Ai;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Codex.Mcp.Tests;

// draft_prose writes with the caller's own AI integration (#36): their default, or the one named by label or id; the
// model defaults to the integration's. The production IntegrationProseGenerator runs over a recording fake provider.
[TestFixture]
[CancelAfter(90000)]
public class McpDraftIntegrationTests
{
    private const string DefaultKey = "sk-ant-mcp-default-AAAA";
    private const string GatewayKey = "sk-mcp-gateway-BBBBBBBB";

    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();
    private readonly RecordingAiProviders _providers = new();
    private McpInProcessServer _server = null!;
    private Guid _gatewayId;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpInProcessServer.StartAsync(
            new FakeProseGenerator([]),
            new Dictionary<string, string?> { [AiSettings.DataProtectionDirKey] = Path.Combine(Path.GetTempPath(), "codex-mcp-keys", Guid.NewGuid().ToString("N")) },
            configure: services =>
            {
                services.RemoveAll<IProseGenerator>();
                services.AddSingleton<IProseGenerator, IntegrationProseGenerator>();
                services.AddSingleton<IAiProviders>(_providers);
            });
        await using var db = new NpgsqlConnection(_server.ConnectionString);
        await db.OpenAsync();
        var protector = _server.Services.GetRequiredService<IAiCredentialProtector>();
        await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Jd, "Mine", DefaultKey, defaultModel: "default-model", isDefault: true);
        _gatewayId = await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Jd, "Home Gateway", GatewayKey, "openai_compatible", "https://llm.example.test/v1", "gateway-model");
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task DraftProse_WhenNoIntegrationIsNamed_ShouldUseTheDefaultAndItsDefaultModel()
    {
        var result = await _server.Client.OkAsync("draft_prose", new { scene = Scene });

        result.GetProperty("model").GetString().ShouldBe("default-model");
        _providers.Connections[^1].ApiKey.ShouldBe(DefaultKey);
        result.ToString().ShouldNotContain(DefaultKey);
    }

    [Test]
    [Order(2)]
    public async Task DraftProse_WhenAnIntegrationIsNamedByLabel_ShouldUseItCaseInsensitively()
    {
        var result = await _server.Client.OkAsync("draft_prose", new { scene = Scene, integration = "home GATEWAY" });

        result.GetProperty("model").GetString().ShouldBe("gateway-model");
        _providers.Connections[^1].Kind.ShouldBe("openai_compatible");
        _providers.Connections[^1].ApiKey.ShouldBe(GatewayKey);
        result.ToString().ShouldNotContain(GatewayKey);
    }

    [Test]
    [Order(3)]
    public async Task DraftProse_WhenAnIntegrationIsNamedById_ShouldUseItWithTheGivenModel()
    {
        var result = await _server.Client.OkAsync("draft_prose", new { scene = Scene, integration = _gatewayId.ToString(), model = "explicit-model" });

        result.GetProperty("model").GetString().ShouldBe("explicit-model");
        _providers.Connections[^1].ApiKey.ShouldBe(GatewayKey);
    }

    [Test]
    [Order(4)]
    public async Task DraftProse_WhenTheIntegrationIsNotOneOfTheCallers_ShouldBeInvalid()
    {
        var calls = _providers.Connections.Count;

        var error = await _server.Client.ErrorAsync("draft_prose", new { scene = Scene, integration = "Nobody's" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("integration");
        _providers.Connections.Count.ShouldBe(calls);
    }

    [Test]
    [Order(5)]
    public async Task DraftProse_WhenANonAnthropicIntegrationHasNoModelAndNoneIsGiven_ShouldBeInvalidWithoutAProviderCall()
    {
        await using var db = new NpgsqlConnection(_server.ConnectionString);
        await db.OpenAsync();
        var protector = _server.Services.GetRequiredService<IAiCredentialProtector>();
        await AiIntegrationRows.InsertAsync(db, protector, SeedIds.Users.Jd, "No Model Gateway", "sk-mcp-nomodel-CCCCCCCC", "openai_compatible", "https://nomodel.example.test/v1");
        var calls = _providers.Connections.Count;

        var error = await _server.Client.ErrorAsync("draft_prose", new { scene = Scene, integration = "No Model Gateway" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("message").GetString()!.ShouldContain("model");
        _providers.Connections.Count.ShouldBe(calls);
    }
}
