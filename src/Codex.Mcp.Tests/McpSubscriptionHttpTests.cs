using System.Text.RegularExpressions;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// subscriptions/listen over the stateless streamable HTTP transport, with personal access tokens from "another machine":
// the held-open response of the listen request carries the acknowledgement and the resources/updated notifications, a
// write through a tool call on a second request (another HTTP client of the same user) announces the scene, and another
// user's token is refused. The test waits on the notification, never sleeps.
[TestFixture]
[CancelAfter(60000)]
public partial class McpSubscriptionHttpTests
{
    private static readonly string Scene = $"codex://scene/{SeedIds.Scenes.Ch4Sc1}";
    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;
    private Guid _userB;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _database = await CodexDatabase.CloneSeedAsync();
        _userB = await TestUsers.CreateAsync(_database.ConnectionString, "b@example.test", "User B");
        _host = CodexHost.Start(_database.ConnectionString,
            services => services.AddTransient<IStartupFilter, RemoteAddressStartupFilter>());
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Test]
    public async Task Listen_WhenASceneIsEditedOnAnotherHttpRequest_ShouldAnnounceItOnTheListenStream()
    {
        await using var listener = await ClientAsync(SeedIds.Users.Jd);
        await using var writer = await ClientAsync(SeedIds.Users.Jd);
        await using var listen = await McpListen.StartAsync(listener, Scene);

        await EditAsync(writer);

        (await listen.NextAsync()).ShouldBe(Scene);
    }

    [Test]
    public async Task Listen_WhenTheTokenIsAnotherUsers_ShouldFailNotFound()
    {
        await using var other = await ClientAsync(_userB);

        var error = await Should.ThrowAsync<McpException>(() => McpListen.StartAsync(other, Scene));

        error.Message.ShouldContain("not_found");
    }

    private async Task<McpClient> ClientAsync(Guid userId)
    {
        var token = await TestTokens.CreateAsync(_database.ConnectionString, userId, "subscribe",
            [ApiTokenScopes.Read, ApiTokenScopes.Write]);
        return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token.Secret}",
                [RemoteAddressStartupFilter.Header] = "203.0.113.7",
            },
        }), cancellationToken: McpCalls.Token);
    }

    private async Task EditAsync(McpClient client)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        var (body, version) = await connection.QuerySingleAsync<(string, int)>(
            "SELECT body, version FROM prose WHERE scene_id = @sceneId", new { sceneId = SeedIds.Scenes.Ch4Sc1 });
        var word = Words().Matches(body).Select(m => m.Value).First(w => Regex.Count(body, Regex.Escape(w)) == 1);
        await client.OkAsync("edit_prose", new
        {
            scene = SeedIds.Scenes.Ch4Sc1.ToString(), expected_version = version, edits = new[] { new { old = word, @new = "subscribed" } },
        });
    }

    [GeneratedRegex(@"\b[A-Za-z]{6,}\b")]
    private static partial Regex Words();
}
