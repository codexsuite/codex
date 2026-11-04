using System.Net;
using System.Net.Http.Json;
using System.Text;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// Streamable HTTP at /mcp on the real web host, without a token (M7, #32): only this machine may use it that way, as
// the local MCP user, in Development (which test hosts are). Anything else without a token gets 401, and must send a
// personal access token (McpTokenHttpTests).
[TestFixture]
[CancelAfter(60000)]
public class McpHttpTransportTests
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";

    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _database = await CodexDatabase.CloneSeedAsync();
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
    public async Task Mcp_WhenRequestComesFromThisMachine_ShouldServeTheTools()
    {
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

        var body = await client.OkAsync("resume_work");

        body.GetProperty("last_scene").GetProperty("item").GetProperty("number").GetString().ShouldBe("5.2");
    }

    [Test]
    public async Task Mcp_WhenRequestComesFromAnotherMachineWithoutAToken_ShouldAnswer401()
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Add(RemoteAddressStartupFilter.Header, "203.0.113.7");

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.ShouldBe(ApiErrorCodes.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenHostHeaderIsNotLocal_ShouldAnswer401()
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Host = "codex.example.com";

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenRequestCameThroughAProxy_ShouldAnswer401()
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestCase("::1")]
    [TestCase("::ffff:127.0.0.1")]
    public async Task Mcp_WhenRequestComesFromAnIpv6LoopbackAddress_ShouldServeIt(string address)
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Add(RemoteAddressStartupFilter.Header, address);

        var response = await http.SendAsync(request, McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestCase("::ffff:203.0.113.7")]
    [TestCase("2001:db8::1")]
    public async Task Mcp_WhenRequestComesFromAnotherMachineOverIpv6_ShouldAnswer401(string address)
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Add(RemoteAddressStartupFilter.Header, address);

        var response = await http.SendAsync(request, McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestCase("http://evil.example")]
    [TestCase("null")]
    [TestCase("http://localhost:1")]
    [TestCase("file:///home/joel/page.html")]
    public async Task Mcp_WhenOriginIsNotThisServer_ShouldAnswer401EvenWithALoopbackHost(string origin)
    {
        // DNS rebinding: a page on another site resolves its own name to 127.0.0.1, so Host is loopback but Origin
        // is the page's.
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Add("Origin", origin);

        var response = await http.SendAsync(request, McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestCase("localhost")]
    [TestCase("127.0.0.1")]
    [TestCase("[::1]")]
    public async Task Mcp_WhenOriginIsThisServersOwnPortOnALoopbackHost_ShouldServeIt(string host)
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Add("Origin", $"http://{host}:{new Uri(_host.BaseUrl).Port}");

        var response = await http.SendAsync(request, McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestCase("Forwarded", "for=203.0.113.7")]
    [TestCase("X-Forwarded-Host", "codex.example.com")]
    [TestCase("X-Real-IP", "203.0.113.7")]
    [TestCase("CF-Connecting-IP", "203.0.113.7")]
    [TestCase("True-Client-IP", "203.0.113.7")]
    [TestCase("Via", "1.1 cloudflared")]
    public async Task Mcp_WhenAnyProxyHeaderIsPresent_ShouldAnswer401(string header, string value)
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Add(header, value);

        var response = await http.SendAsync(request, McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [TestCase("GET", null)]
    [TestCase("DELETE", null)]
    [TestCase("GET", "203.0.113.7")]
    [TestCase("DELETE", "203.0.113.7")]
    public async Task Mcp_WhenGetOrDeleteIsSent_ShouldBeRefusedOrUnmapped(string method, string? remote)
    {
        // Stateless streamable HTTP has no stream to GET and no session to DELETE; from another machine without a token
        // the guard refuses first.
        using var http = _host.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/mcp");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (remote != null)
        {
            request.Headers.Add(RemoteAddressStartupFilter.Header, remote);
        }

        var response = await http.SendAsync(request, McpCalls.Token);

        response.IsSuccessStatusCode.ShouldBeFalse();
        if (remote != null)
        {
            response.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }
    }

    [Test]
    public async Task Mcp_WhenTwoHttpClientsRead_ShouldNotShareStaleRefMemory()
    {
        // HTTP is stateless (no session id), so there is no session to be stale against: client B never gets
        // stale_ref from what client A read, and "4.1" names what it names now.
        var book = SeedIds.Books.ProductEngineering.ToString();
        await using var clientA = await HttpMcpClientAsync();
        await using var clientB = await HttpMcpClientAsync();
        await clientA.OkAsync("get_outline", new { book });
        await clientB.OkAsync("get_outline", new { book });
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        var before = await connection.ExecuteScalarAsync<string>("SELECT position FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch4Sc2 });
        await connection.ExecuteAsync("UPDATE scene SET position = '0' WHERE id = @id", new { id = SeedIds.Scenes.Ch4Sc2 });
        try
        {
            var a = await clientA.OkAsync("read_manuscript", new { book, scope = "4.1" });
            var b = await clientB.OkAsync("read_manuscript", new { book, scope = "4.1" });

            a.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch4Sc2);
            b.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch4Sc2);
        }
        finally
        {
            await connection.ExecuteAsync("UPDATE scene SET position = @before WHERE id = @id", new { before, id = SeedIds.Scenes.Ch4Sc2 });
        }
    }

    private Task<McpClient> HttpMcpClientAsync() => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
        TransportMode = HttpTransportMode.StreamableHttp,
    }), cancellationToken: McpCalls.Token);

    private static HttpRequestMessage InitializeRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(Initialize, Encoding.UTF8, "application/json") };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }
}
