using Codex.Application.Identity;
using Codex.Application.Mcp;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Codex.TestSupport.Fakes;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests;

// The spend cap (#32, from the #20 review): each codex:generate token may start CODEX_MCP_MAX_GENERATIONS Codex
// generations in a sliding hour (here 2); the next is rate_limited. Each token has its own allowance.
[TestFixture]
[CancelAfter(60000)]
public class McpTokenGenerateCapTests
{
    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();

    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _database = await CodexDatabase.CloneSeedAsync();
        _host = CodexHost.Start(_database.ConnectionString, services =>
        {
            services.RemoveAll<IProseGenerator>();
            services.AddSingleton<IProseGenerator>(new FakeProseGenerator(["Words."]));
            services.RemoveAll<McpGenerationBudget>();
            services.AddSingleton(new McpGenerationBudget(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [McpGenerationBudget.Setting] = "2" }).Build(),
                TimeProvider.System));
        });
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Test]
    public async Task DraftProse_WhenATokenHasStartedItsHourlyCap_ShouldRateLimitItButNotAnotherToken()
    {
        await using var first = await ClientAsync();
        await using var second = await ClientAsync();
        await first.OkAsync("draft_prose", new { scene = Scene });
        await first.OkAsync("draft_prose", new { scene = Scene });

        var error = await first.ErrorAsync("draft_prose", new { scene = Scene });
        var other = await second.OkAsync("draft_prose", new { scene = Scene });

        error.GetProperty("code").GetString().ShouldBe("rate_limited");
        other.GetProperty("generations_left").GetInt32().ShouldBe(1);
    }

    private async Task<McpClient> ClientAsync()
    {
        var token = await TestTokens.CreateAsync(_database.ConnectionString, SeedIds.Users.Jd, "Drafting agent",
            [ApiTokenScopes.Read, ApiTokenScopes.Generate]);
        return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token.Secret}" },
        }), cancellationToken: McpCalls.Token);
    }
}
