using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codex.Mcp.Tests;

// Outside Development, /mcp has no fixed local user (#32): a request from this machine without a token gets 401 like any
// other, and a token works.
[TestFixture]
[CancelAfter(60000)]
public class McpHttpProductionTests
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";

    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _database = await CodexDatabase.CloneSeedAsync();
        // The test host runs as Development; the guard reads the environment per request, so renaming it is enough.
        _host = CodexHost.Start(_database.ConnectionString, services =>
            ((IHostEnvironment)services.Last(d => d.ServiceType == typeof(IHostEnvironment)).ImplementationInstance!).EnvironmentName =
            Environments.Production);
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Test]
    public async Task Mcp_WhenThisMachineSendsNoTokenOutsideDevelopment_ShouldAnswer401()
    {
        var response = await InitializeAsync(null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenThisMachineSendsATokenOutsideDevelopment_ShouldServeIt()
    {
        var token = await TestTokens.CreateAsync(_database.ConnectionString, SeedIds.Users.Jd, "Claude Code", [ApiTokenScopes.Read]);

        var response = await InitializeAsync(token.Secret);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> InitializeAsync(string? secret)
    {
        using var http = _host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(Initialize, Encoding.UTF8, "application/json") };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (secret != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }
        return await http.SendAsync(request, McpCalls.Token);
    }
}
