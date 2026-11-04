using System.Text.RegularExpressions;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// edit_prose over stdio on Ch 4 · Sc 1: the negative paths write nothing, a good edit cuts the session's 'agent'
// revision, and undo puts the body back. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(60000)]
public partial class McpEditProseTests
{
    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();
    private McpStdioServer _server = null!;
    private string _seedBody = "";
    private int _seedVersion;
    private string _phrase = "";
    private string _undoId = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        (_seedBody, _seedVersion) = await ProseAsync();
        // A run of plain words that occurs exactly once in the scene.
        _phrase = PlainWords().Matches(_seedBody).Select(m => m.Value.Trim())
            .First(p => Regex.Count(_seedBody, Regex.Escape(p)) == 1);
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task EditProse_WhenExpectedVersionIsStale_ShouldConflictAndWriteNothing()
    {
        var error = await Client.ErrorAsync("edit_prose", new
        {
            scene = Scene, expected_version = _seedVersion - 1, edits = new[] { new { old = _phrase, @new = "changed" } },
        });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        error.GetProperty("message").GetString()!.ShouldContain($"version {_seedVersion}");
        (await ProseAsync()).ShouldBe((_seedBody, _seedVersion));
    }

    [Test]
    [Order(2)]
    public async Task EditProse_WhenOneOldIsMissing_ShouldFailTheWholeCall()
    {
        var error = await Client.ErrorAsync("edit_prose", new
        {
            scene = Scene, expected_version = _seedVersion,
            edits = new[] { new { old = _phrase, @new = "changed" }, new { old = "words that are not in the scene", @new = "x" } },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("edits[1].old");
        (await ProseAsync()).ShouldBe((_seedBody, _seedVersion));
    }

    [Test]
    [Order(3)]
    public async Task EditProse_WhenEditAddsARefLink_ShouldFailInvalid()
    {
        var error = await Client.ErrorAsync("edit_prose", new
        {
            scene = Scene, expected_version = _seedVersion,
            edits = new[] { new { old = _phrase, @new = $"[{_phrase}](ref:{Guid.CreateVersion7()})" } },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        (await ProseAsync()).ShouldBe((_seedBody, _seedVersion));
    }

    [Test]
    [Order(4)]
    public async Task EditProse_WhenDryRun_ShouldReturnTheNewBodyAndWriteNothing()
    {
        var body = await Client.OkAsync("edit_prose", new
        {
            scene = Scene, expected_version = _seedVersion, dry_run = true, edits = new[] { new { old = _phrase, @new = "MCP was here" } },
        });

        body.GetProperty("body").GetString().ShouldBe(_seedBody.Replace(_phrase, "MCP was here", StringComparison.Ordinal));
        (await ProseAsync()).ShouldBe((_seedBody, _seedVersion));
    }

    [Test]
    [Order(5)]
    public async Task EditProse_WhenApplied_ShouldWriteAndCutTheAgentRevisionOfTheOldBody()
    {
        var body = await Client.OkAsync("edit_prose", new
        {
            scene = Scene, expected_version = _seedVersion, edits = new[] { new { old = _phrase, @new = "MCP was here" } },
        });

        body.GetProperty("version").GetInt32().ShouldBe(_seedVersion + 1);
        _undoId = body.GetProperty("undo_id").GetString()!;
        (await ProseAsync()).ShouldBe((_seedBody.Replace(_phrase, "MCP was here", StringComparison.Ordinal), _seedVersion + 1));
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        var revisions = (await connection.QueryAsync<string>(
            "SELECT body FROM prose_revision WHERE scene_id = @sceneId AND source = 'agent' AND mcp_session = 'stdio' AND client_name IS NOT NULL",
            new { sceneId = SeedIds.Scenes.Ch4Sc1 })).ToList();
        revisions.ShouldBe([_seedBody]);
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenGivenTheEditsUndoId_ShouldRestoreTheBody()
    {
        await Client.OkAsync("undo", new { undo_id = _undoId });

        (await ProseAsync()).ShouldBe((_seedBody, _seedVersion + 2));
    }

    private async Task<(string Body, int Version)> ProseAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>(
            "SELECT body, version FROM prose WHERE scene_id = @sceneId", new { sceneId = SeedIds.Scenes.Ch4Sc1 });
    }

    [GeneratedRegex(@"(?<=\s)[A-Za-z]+(?: [A-Za-z]+){3}(?=[ ,.])")]
    private static partial Regex PlainWords();
}
