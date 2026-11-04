using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Hosting;

namespace Codex.Mcp.Tests;

// export_for_printer is on codex:read for preflight (it queues nothing) and asks for codex:write inside the tool for queue;
// preview_layout likewise for kind proof. A stdio process holding only codex:read.
[TestFixture]
[CancelAfter(120000)]
public class McpPrintScopesTests
{
    private static readonly string Book = SeedIds.Books.Undertow.ToString();
    private McpStdioServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartWithTokenAsync(
        async database => (await TestTokens.CreateAsync(database.ConnectionString, SeedIds.Users.Jd, "Read only", [ApiTokenScopes.Read])).Secret,
        JourneyServer.TemplatesEnvironment());

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task ExportForPrinter_WhenTheTokenOnlyReads_ShouldPreflightButNotQueue()
    {
        var preflight = await _server.Client.OkAsync("export_for_printer", new { book = Book, mode = "preflight" });
        var error = await _server.Client.ErrorAsync("export_for_printer", new { book = Book, mode = "queue" });

        preflight.GetProperty("can_queue").GetBoolean().ShouldBeTrue();
        error.GetProperty("code").GetString().ShouldBe("forbidden");
        error.GetProperty("field").GetString().ShouldBe(ApiTokenScopes.Write);
        (await _server.Client.OkAsync("get_clearance", new { book = Book })).GetProperty("ok").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task PreviewLayout_WhenTheTokenOnlyReads_ShouldNotProof()
    {
        var error = await _server.Client.ErrorAsync("preview_layout", new { book = Book, scope = "1", kind = "proof" });

        error.GetProperty("code").GetString().ShouldBe("forbidden");
        error.GetProperty("field").GetString().ShouldBe(ApiTokenScopes.Write);
    }
}
