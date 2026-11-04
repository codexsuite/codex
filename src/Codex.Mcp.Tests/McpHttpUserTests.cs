using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// The web host's /mcp acts as the configured MCP user (McpUser, CODEX_MCP_USER_EMAIL), not as whoever the host's
// browser session is: here user B, so JD's books are not found over it (#29).
[TestFixture]
[CancelAfter(60000)]
public class McpHttpUserTests
{
    private const string UserBEmail = "b@example.test";

    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _database = await CodexDatabase.CloneSeedAsync();
        await TestUsers.CreateAsync(_database.ConnectionString, UserBEmail, "User B");
        _host = CodexHost.Start(_database.ConnectionString, services =>
        {
            services.RemoveAll<McpUser>();
            services.AddSingleton(sp => new McpUser(
                sp.GetRequiredService<NpgsqlDataSource>(),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [McpUser.EmailKey] = UserBEmail }).Build()));
        });
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Test]
    public async Task Mcp_WhenTheMcpUserIsB_ShouldNotFindJdsBook()
    {
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }), cancellationToken: McpCalls.Token);

        var books = await client.OkAsync("list_books");
        var error = await client.ErrorAsync("read_manuscript", new { book = SeedIds.Books.HollowThrone.ToString() });

        books.ToString().ShouldNotContain(SeedIds.Books.HollowThrone.ToString());
        error.GetProperty("code").GetString().ShouldBe("not_found");
    }
}
