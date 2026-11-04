using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// list_revisions over stdio on the Product Engineering seed (#20 review 2, "restore an old version": read first). One scenario
// on its own clone: an agent edit to Ch 4 · Sc 1 cuts a revision holding the text before it, which the list shows as the
// agent's, and which `revision` returns word for word; an unknown revision is not_found.
[TestFixture]
[CancelAfter(120000)]
public class McpListRevisionsTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ListRevisions_WhenTheAgentEditedTheScene_ShouldListItsRevisionAndReturnTheTextBeforeTheEdit()
    {
        string before;
        await using (var connection = new NpgsqlConnection(_server.ConnectionString))
        {
            before = await connection.ExecuteScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.Ch4Sc1 }) ?? "";
        }
        var read = await Client.OkAsync("read_manuscript", new { book = Book, scope = "4.1", format = "markdown" });
        var version = read.GetProperty("scenes")[0].GetProperty("version").GetInt32();
        var word = before.Split(' ', StringSplitOptions.RemoveEmptyEntries).First(w => w.Length > 6 && w.All(char.IsLetter));
        await Client.OkAsync("edit_prose", new { scene = "4.1", book = Book, expected_version = version, edits = new[] { new { old = word, @new = word + "s", occurrence = 1 } } });

        var list = await Client.OkAsync("list_revisions", new { scene = "4.1", book = Book });

        list.GetProperty("version").GetInt32().ShouldBe(version + 1);
        list.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Scenes.Ch4Sc1);
        var revisions = list.GetProperty("revisions").EnumerateArray().ToList();
        // Whether any row holds the edited text depends on the autosave throttle (none within ten minutes of the seed's
        // import revision, as on a freshly built template), so only "at most one" is fixed.
        revisions.Count(r => r.GetProperty("current").GetBoolean()).ShouldBeLessThanOrEqualTo(1);
        var agent = revisions.First(r => r.GetProperty("source").GetString() == "agent");
        agent.GetProperty("current").GetBoolean().ShouldBeFalse();
        list.TryGetProperty("revision", out var none).ShouldBeFalse(none.ToString());

        var one = await Client.OkAsync("list_revisions", new { scene = SeedIds.Scenes.Ch4Sc1.ToString(), revision = agent.GetProperty("id").GetGuid() });

        one.GetProperty("revision").GetProperty("markdown").GetString().ShouldBe(before);
    }

    [Test]
    [Order(2)]
    public async Task ListRevisions_WhenTheRevisionIsUnknown_ShouldFailNotFound()
    {
        var error = await Client.ErrorAsync("list_revisions", new { scene = "4.1", book = Book, revision = Guid.NewGuid() });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }
}
