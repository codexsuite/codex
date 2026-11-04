using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests;

// Provenance is a command parameter (Joel, 2026-10-22): the References commands write the History line's source and the client's
// name when they insert it. The same command called by an MCP tool writes 'agent' with the client's name; called by the web
// endpoint it writes 'user' with no client. Nothing relabels a line afterwards.
[TestFixture]
[CancelAfter(120000)]
public class McpEventProvenanceTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task Reopen_WhenCalledByAnMcpTool_ShouldWriteTheLineAsTheAgentWithTheClientsName()
    {
        await Client.OkAsync("record_clearance_decision", new { decision = "reopen", source = "#1", book = Book });

        var line = await _server.ScalarAsync<string>(
            "SELECT source || '/' || coalesce(client_name, '') FROM reference_event WHERE reference_id = @id AND action = 'reopened'", new { id = SeedIds.References.BeckXp });
        line.ShouldStartWith("agent/");
        line.Length.ShouldBeGreaterThan("agent/".Length);
    }

    [Test]
    [Order(2)]
    public async Task Reopen_WhenCalledByTheWebEndpoint_ShouldWriteTheLineAsTheUserWithNoClient()
    {
        await _server.PostApiAsync($"/api/references/{SeedIds.References.ConwayCommittees}/reopen");

        var line = await _server.ScalarAsync<string>(
            "SELECT source || '/' || coalesce(client_name, '') FROM reference_event WHERE reference_id = @id AND action = 'reopened'", new { id = SeedIds.References.ConwayCommittees });
        line.ShouldBe("user/");
    }
}
