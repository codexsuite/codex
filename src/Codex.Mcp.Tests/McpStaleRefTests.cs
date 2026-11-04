using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using Npgsql;

namespace Codex.Mcp.Tests;

// Display numbers are resolved when a call runs (#20 2.1). When "4.1" names another scene than it did when this
// session last read the book, the call fails with stale_ref (M11) rather than reading the wrong scene.
[TestFixture]
[CancelAfter(60000)]
public class McpStaleRefTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ReadManuscript_WhenTheNumberStillNamesTheSameScene_ShouldRead()
    {
        await _server.Client.OkAsync("get_outline", new { book = ProductEngineering });

        var body = await _server.Client.OkAsync("read_manuscript", new { book = ProductEngineering, scope = "4.1" });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch4Sc1);
    }

    [Test]
    [Order(2)]
    public async Task ReadManuscript_WhenTheNumberNowNamesAnotherScene_ShouldFailStaleRefWithBoth()
    {
        // Someone moves Ch 4 Sc 2 in front of Sc 1 in the UI: "4.1" is now the other scene.
        await using (var connection = new NpgsqlConnection(_server.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE scene SET position = '0' WHERE id = @id", new { id = SeedIds.Scenes.Ch4Sc2 });
        }

        var error = await _server.Client.ErrorAsync("read_manuscript", new { book = ProductEngineering, scope = "4.1" });

        error.GetProperty("code").GetString().ShouldBe("stale_ref");
        error.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())
            .ShouldBe([SeedIds.Scenes.Ch4Sc1, SeedIds.Scenes.Ch4Sc2]);
    }

    [Test]
    [Order(3)]
    public async Task ReadManuscript_WhenRetriedAfterStaleRef_ShouldReadWhatTheNumberNamesNow()
    {
        var body = await _server.Client.OkAsync("read_manuscript", new { book = ProductEngineering, scope = "4.1" });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch4Sc2);
    }
}
