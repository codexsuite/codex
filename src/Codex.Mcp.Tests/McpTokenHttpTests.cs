using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Codex.Application.Identity;
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

// /mcp with personal access tokens (#32 phase 1): a token acts as its user, from any machine, with only its scopes. A
// missing, unknown, revoked or expired token, or a disabled user's, gets 401 with a Bearer challenge. The requests
// below come "from another machine" (RemoteAddressStartupFilter), so the localhost fallback never applies.
[TestFixture]
[CancelAfter(60000)]
public class McpTokenHttpTests
{
    private const string Remote = "203.0.113.7";
    private const string UserBEmail = "b@example.test";

    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";

    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;
    private Guid _userB;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _database = await CodexDatabase.CloneSeedAsync();
        _userB = await TestUsers.CreateAsync(_database.ConnectionString, UserBEmail, "User B");
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
    public async Task Mcp_WhenAReadTokenCallsAReadTool_ShouldServeItFromAnotherMachine()
    {
        var token = await TokenAsync(SeedIds.Users.Jd, ApiTokenScopes.Read);
        await using var client = await ClientAsync(token.Secret);

        var body = await client.OkAsync("resume_work");

        body.GetProperty("last_scene").GetProperty("item").GetProperty("number").GetString().ShouldBe("5.2");
    }

    [Test]
    public async Task Mcp_WhenAReadTokenCallsAWriteTool_ShouldBeForbiddenNamingTheScope()
    {
        var token = await TokenAsync(SeedIds.Users.Jd, ApiTokenScopes.Read);
        await using var client = await ClientAsync(token.Secret);

        var error = await client.ErrorAsync("edit_outline", new { book = SeedIds.Books.ProductEngineering.ToString(), ops = Array.Empty<object>() });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        error.GetProperty("message").GetString()!.ShouldContain(ApiTokenScopes.Write);
        error.GetProperty("field").GetString().ShouldBe(ApiTokenScopes.Write);
    }

    [TestCase("cite_source", ApiTokenScopes.References)]
    [TestCase("draft_prose", ApiTokenScopes.Generate)]
    public async Task Mcp_WhenAWriteTokenCallsAToolOfAnotherScope_ShouldBeForbidden(string tool, string scope)
    {
        var token = await TokenAsync(SeedIds.Users.Jd, ApiTokenScopes.Read, ApiTokenScopes.Write);
        await using var client = await ClientAsync(token.Secret);

        var error = await client.ErrorAsync(tool, new { scene = SeedIds.Scenes.Ch4Sc1.ToString() });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        error.GetProperty("field").GetString().ShouldBe(scope);
    }

    [Test]
    public async Task Mcp_WhenUserBsTokenNamesJdsBook_ShouldNotFindIt()
    {
        var token = await TokenAsync(_userB, ApiTokenScopes.Read);
        await using var client = await ClientAsync(token.Secret);

        var books = await client.OkAsync("list_books");
        var error = await client.ErrorAsync("read_manuscript", new { book = SeedIds.Books.HollowThrone.ToString() });

        books.ToString().ShouldNotContain(SeedIds.Books.HollowThrone.ToString());
        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task Mcp_WhenAWriteTokenWrites_ShouldRecordTheTokenOnTheWrite()
    {
        var token = await TokenAsync(SeedIds.Users.Jd, ApiTokenScopes.Read, ApiTokenScopes.Write);
        await using var client = await ClientAsync(token.Secret);

        var draft = await client.OkAsync("submit_draft", new { scene = SeedIds.Scenes.Ch4Sc1.ToString(), text = "Written by an agent." });

        var generation = Guid.Parse(draft.GetProperty("generation").GetString()!);
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        var change = await connection.QuerySingleAsync<(Guid? TokenId, string? TokenName)>(
            "SELECT api_token_id, api_token_name FROM change_set WHERE tool = 'submit_draft' AND api_token_id = @id", new { id = token.Id });
        var stored = await connection.QuerySingleAsync<(Guid? TokenId, string? TokenName)>(
            "SELECT api_token_id, api_token_name FROM ai_generation WHERE id = @generation", new { generation });
        change.ShouldBe((token.Id, TokenName));
        stored.ShouldBe((token.Id, TokenName));
    }

    [Test]
    public async Task Mcp_WhenNoTokenIsSentFromAnotherMachine_ShouldAnswer401WithABearerChallenge()
    {
        var response = await InitializeAsync(null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Single().Scheme.ShouldBe("Bearer");
        (await response.Content.ReadFromJsonAsync<ApiError>())!.Code.ShouldBe(ApiErrorCodes.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenTheTokenIsUnknown_ShouldAnswer401()
    {
        var token = await TokenAsync(SeedIds.Users.Jd, ApiTokenScopes.Read);

        var response = await InitializeAsync(token.Secret[..^1] + (token.Secret[^1] == 'a' ? 'b' : 'a'));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenTheTokenIsRevoked_ShouldAnswer401()
    {
        var token = await TokenAsync(SeedIds.Users.Jd, ApiTokenScopes.Read);
        (await InitializeAsync(token.Secret)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await TestTokens.RevokeAsync(_database.ConnectionString, token.Id);

        (await InitializeAsync(token.Secret)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenTheTokenHasExpired_ShouldAnswer401()
    {
        var token = await TestTokens.CreateAsync(_database.ConnectionString, SeedIds.Users.Jd, TokenName, [ApiTokenScopes.Read],
            TimeProvider.System.GetUtcNow().AddMinutes(-1));

        var response = await InitializeAsync(token.Secret);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenTheTokensUserIsDisabled_ShouldAnswer401()
    {
        var userC = await TestUsers.CreateAsync(_database.ConnectionString, "c@example.test", "User C");
        var token = await TokenAsync(userC, ApiTokenScopes.Read);
        (await InitializeAsync(token.Secret)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var connection = new NpgsqlConnection(_database.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE app_user SET disabled_at = now() WHERE id = @userC", new { userC });
        }

        (await InitializeAsync(token.Secret)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mcp_WhenTheAuthorizationIsNotABearerToken_ShouldAnswer401()
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("jd@codex.local:codex-dev-password")));

        var response = await http.SendAsync(request, McpCalls.Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private const string TokenName = "Claude Code on the desktop";

    private Task<TestTokens.Created> TokenAsync(Guid userId, params string[] scopes)
        => TestTokens.CreateAsync(_database.ConnectionString, userId, TokenName, scopes);

    private Task<McpClient> ClientAsync(string secret) => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri($"{_host.BaseUrl}/mcp"),
        TransportMode = HttpTransportMode.StreamableHttp,
        AdditionalHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {secret}",
            [RemoteAddressStartupFilter.Header] = Remote,
        },
    }), cancellationToken: McpCalls.Token);

    private async Task<HttpResponseMessage> InitializeAsync(string? secret)
    {
        using var http = _host.CreateClient();
        using var request = InitializeRequest();
        if (secret != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }
        return await http.SendAsync(request, McpCalls.Token);
    }

    private static HttpRequestMessage InitializeRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(Initialize, Encoding.UTF8, "application/json") };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add(RemoteAddressStartupFilter.Header, Remote);
        return request;
    }
}
