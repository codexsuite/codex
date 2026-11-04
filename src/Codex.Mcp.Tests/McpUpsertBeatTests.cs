using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// upsert_beat on Ch 4 · Sc 1: a stale version or a bad argument writes nothing, a beat is added and undone, and undo of
// an update puts a cleared target word count back to null. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(60000)]
public class McpUpsertBeatTests
{
    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();
    private McpStdioServer _server = null!;
    private string _body = "";
    private int _version;
    private string _beat = "";
    private string _createUndo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        (_body, _version) = await ProseAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task UpsertBeat_WhenTheVersionIsStale_ShouldConflictAndWriteNothing()
    {
        var error = await Client.ErrorAsync("upsert_beat", new { scene = Scene, at = "end", expected_version = _version - 1, instruction = "x" });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ProseAsync()).ShouldBe((_body, _version));
    }

    [Test]
    [Order(2)]
    public async Task UpsertBeat_WhenAContextEntryIsUnknown_ShouldFailBeforeWritingAnything()
    {
        var error = await Client.ErrorAsync("upsert_beat", new
        {
            scene = Scene, at = "end", expected_version = _version, instruction = "x", context_add = new[] { "No Such Entry" },
        });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ProseAsync()).ShouldBe((_body, _version));
    }

    [Test]
    [Order(3)]
    public async Task UpsertBeat_WhenTheTargetWordCountIsNotPositive_ShouldFailBeforeWritingAnything()
    {
        var error = await Client.ErrorAsync("upsert_beat", new { scene = Scene, at = "end", expected_version = _version, target_word_count = 0 });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("target_word_count");
        (await ProseAsync()).ShouldBe((_body, _version));
    }

    [Test]
    [Order(4)]
    public async Task UpsertBeat_WhenCreating_ShouldAddTheBeatLine()
    {
        var result = await Client.OkAsync("upsert_beat", new { scene = Scene, at = "end", expected_version = _version, instruction = "A new beat." });

        _beat = result.GetProperty("beat").GetProperty("id").GetString()!;
        _createUndo = result.GetProperty("undo_id").GetString()!;
        (await ProseAsync()).Body.ShouldContain($"::beat{{id=\"{_beat}\"}}");
    }

    [Test]
    [Order(5)]
    public async Task Undo_WhenAnUpdateSetATargetOnABeatWithNone_ShouldPutNullBack()
    {
        var update = await Client.OkAsync("upsert_beat", new { beat = _beat, target_word_count = 300 });
        (await TargetAsync()).ShouldBe(300);

        await Client.OkAsync("undo", new { undo_id = update.GetProperty("undo_id").GetString() });

        (await TargetAsync()).ShouldBeNull();
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenTheBeatWasAdded_ShouldTakeTheLineAway()
    {
        await Client.OkAsync("undo", new { undo_id = _createUndo });

        (await ProseAsync()).Body.ShouldBe(_body);
    }

    private async Task<int?> TargetAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int?>("SELECT target_word_count FROM beat WHERE id = @id", new { id = Guid.Parse(_beat) });
    }

    private async Task<(string Body, int Version)> ProseAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>(
            "SELECT body, version FROM prose WHERE scene_id = @sceneId", new { sceneId = SeedIds.Scenes.Ch4Sc1 });
    }
}
