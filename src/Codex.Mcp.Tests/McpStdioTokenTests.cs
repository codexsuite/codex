using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Hosting;

namespace Codex.Mcp.Tests;

// The stdio process started with CODEX_MCP_TOKEN (#32): it acts as the token's user with the token's scopes, and checks
// the token again on every call, so revoking it stops the running session.
[TestFixture]
[CancelAfter(120000)]
public class McpStdioTokenTests
{
    private McpStdioServer _server = null!;
    private Guid _tokenId;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartWithTokenAsync(async database =>
    {
        var token = await TestTokens.CreateAsync(database.ConnectionString, SeedIds.Users.Jd, "Claude Desktop", [ApiTokenScopes.Read]);
        _tokenId = token.Id;
        return token.Secret;
    });

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ResumeWork_WhenTheTokenCanRead_ShouldServeIt()
    {
        var body = await _server.Client.OkAsync("resume_work");

        body.GetProperty("last_scene").GetProperty("item").GetProperty("number").GetString().ShouldBe("5.2");
    }

    [Test]
    [Order(2)]
    public async Task EditOutline_WhenTheTokenCannotWrite_ShouldBeForbiddenNamingTheScope()
    {
        var error = await _server.Client.ErrorAsync("edit_outline", new { book = SeedIds.Books.ProductEngineering.ToString(), ops = Array.Empty<object>() });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        error.GetProperty("message").GetString()!.ShouldContain(ApiTokenScopes.Write);
    }

    [Test]
    [Order(3)]
    public async Task ResumeWork_WhenTheTokenIsRevokedMidSession_ShouldBeUnauthorized()
    {
        await TestTokens.RevokeAsync(_server.ConnectionString, _tokenId);

        var error = await _server.Client.ErrorAsync("resume_work");

        error.GetProperty("code").GetString().ShouldBe("unauthorized");
    }
}
