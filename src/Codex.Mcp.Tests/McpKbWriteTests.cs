using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// upsert_kb_entry and link_kb_entry over stdio on the Product Engineering seed: a duplicate name is ambiguous, a new
// entry links to a scene and a mention, a series entry from another series is out_of_scope, and undo takes each back.
// One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(60000)]
public class McpKbWriteTests
{
    private static readonly string Scene = SeedIds.Scenes.Ch4Sc1.ToString();
    private static readonly string MentionScene = SeedIds.Scenes.Ch5Sc1.ToString();
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private string _entry = "";
    private string _createUndo = "";
    private string _linkUndo = "";
    private const string Phrase = "cautionary tale";
    private string _bodyBefore = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        (_bodyBefore, _) = await ProseAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task UpsertKbEntry_WhenTheNameBelongsToAnotherEntry_ShouldBeAmbiguousAndWriteNothing()
    {
        var error = await Client.ErrorAsync("upsert_kb_entry", new { book = Book, name = "Goodhart's Law", category = "Laws" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        error.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetString())
            .ShouldContain(SeedIds.KbEntries.GoodhartsLaw.ToString());
    }

    [Test]
    [Order(2)]
    public async Task UpsertKbEntry_WhenAnAliasBelongsToAnotherEntry_ShouldBeAmbiguousUnlessDuplicatesAreAllowed()
    {
        var error = await Client.ErrorAsync("upsert_kb_entry", new { book = Book, name = "Team Topologies", category = "Research", aliases = new[] { "Goodhart" } });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
    }

    [Test]
    [Order(3)]
    public async Task UpsertKbEntry_WhenNew_ShouldCreateItInTheCategory()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new
        {
            book = Book, name = "Team Topologies", category = "Research", aliases = new[] { "team types" }, summary = "Four team types.",
        });

        _entry = result.GetProperty("item").GetProperty("id").GetString()!;
        _createUndo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("created").GetBoolean().ShouldBeTrue();
        result.GetProperty("category").GetString().ShouldBe("Research");
        result.GetProperty("scope").GetString().ShouldBe("book");
        result.GetProperty("item").GetProperty("open_url").GetString()!.ShouldContain($"entry={_entry}");
    }

    [Test]
    [Order(4)]
    public async Task LinkKbEntry_WhenTheEntryIsFromAnotherSeries_ShouldFailThatLinkOutOfScope()
    {
        var result = await Client.OkAsync("link_kb_entry", new
        {
            entry = SeedIds.KbEntries.Gravetouch.ToString(), links = new[] { new { @as = "scene", scene = Scene } },
        });

        result.GetProperty("failed").GetInt32().ShouldBe(1);
        var error = result.GetProperty("links")[0].GetProperty("error");
        error.GetProperty("code").GetString().ShouldBe("out_of_scope");
        result.TryGetProperty("undo_id", out _).ShouldBeFalse();
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM scene_kb WHERE scene_id = @scene AND kb_entry_id = @entry AND archived_at IS NULL)",
            new { scene = SeedIds.Scenes.Ch4Sc1, entry = SeedIds.KbEntries.Gravetouch })).ShouldBeFalse();
    }

    [Test]
    [Order(5)]
    public async Task LinkKbEntry_WhenInScope_ShouldLinkTheSceneAndWrapTheMention()
    {
        var result = await Client.OkAsync("link_kb_entry", new
        {
            entry = _entry,
            links = new object[] { new { @as = "scene", scene = Scene }, new { @as = "mention", scene = MentionScene, anchor = new { text = Phrase } } },
        });

        _linkUndo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("failed").GetInt32().ShouldBe(0, result.ToString());
        (await ProseAsync()).Body.ShouldContain($"[{Phrase}](kb:{_entry})");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM scene_kb WHERE scene_id = @scene AND kb_entry_id = @entry AND archived_at IS NULL)",
            new { scene = SeedIds.Scenes.Ch4Sc1, entry = Guid.Parse(_entry) })).ShouldBeTrue();
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenTheLinksAreUndone_ShouldUnlinkTheSceneAndPutTheProseBack()
    {
        await Client.OkAsync("undo", new { undo_id = _linkUndo });

        (await ProseAsync()).Body.ShouldBe(_bodyBefore);
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM scene_kb WHERE scene_id = @scene AND kb_entry_id = @entry AND archived_at IS NULL)",
            new { scene = SeedIds.Scenes.Ch4Sc1, entry = Guid.Parse(_entry) })).ShouldBeFalse();
        // Undoing the link archives the row rather than deleting it.
        (await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM scene_kb WHERE scene_id = @scene AND kb_entry_id = @entry AND archived_at IS NOT NULL)",
            new { scene = SeedIds.Scenes.Ch4Sc1, entry = Guid.Parse(_entry) })).ShouldBeTrue();
    }

    [Test]
    [Order(7)]
    public async Task UpsertKbEntry_WhenUpdatingTheSummary_ShouldChangeItAndUndoShouldPutItBack()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new { entry = _entry, book = Book, summary = "Changed." });

        result.GetProperty("created").GetBoolean().ShouldBeFalse();
        (await SummaryAsync()).ShouldBe("Changed.");
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await SummaryAsync()).ShouldBe("Four team types.");
    }

    [Test]
    [Order(7)]
    public async Task UpsertKbEntry_WhenSettingTagsAndExcludeFromAutoContext_ShouldStoreReturnAndUndoThem()
    {
        var result = await Client.OkAsync("upsert_kb_entry",
            new { entry = _entry, book = Book, tags = new[] { "framework", "teams" }, exclude_from_auto_context = true });

        var read = await Client.OkAsync("get_kb_entry", new { entry = _entry });
        read.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["framework", "teams"]);
        read.GetProperty("exclude_from_auto_context").GetBoolean().ShouldBeTrue();
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        var back = await Client.OkAsync("get_kb_entry", new { entry = _entry });
        back.GetProperty("tags").GetArrayLength().ShouldBe(0);
        back.GetProperty("exclude_from_auto_context").GetBoolean().ShouldBeFalse();
    }

    [Test]
    [Order(8)]
    public async Task Undo_WhenTheEntryWasCreated_ShouldArchiveIt()
    {
        await Client.OkAsync("undo", new { undo_id = _createUndo });

        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<bool>(
            "SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @entry", new { entry = Guid.Parse(_entry) })).ShouldBeTrue();
    }

    [Test]
    [Order(9)]
    public async Task EditOutline_WhenASummaryNamesAnEntryWithAt_ShouldStoreAMentionAndRefuseAnUnknownName()
    {
        var unknown = await Client.ErrorAsync("edit_outline", new
        {
            book = Book, ops = new[] { new { op = "update", item = Scene, summary = "Nobody like @[Nobody Known] appears." } },
        });
        await Client.OkAsync("edit_outline", new
        {
            book = Book, ops = new[] { new { op = "update", item = Scene, summary = "Shows @[Goodhart's Law] at work." } },
        });

        unknown.GetProperty("code").GetString().ShouldBe("not_found");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.QuerySingleAsync<string>("SELECT summary FROM scene WHERE id = @scene", new { scene = SeedIds.Scenes.Ch4Sc1 }))
            .ShouldBe($"Shows [Goodhart's Law](kb:{SeedIds.KbEntries.GoodhartsLaw}) at work.");
    }

    private async Task<(string Body, int Version)> ProseAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>(
            "SELECT body, version FROM prose WHERE scene_id = @sceneId", new { sceneId = SeedIds.Scenes.Ch5Sc1 });
    }

    private async Task<string> SummaryAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<string>("SELECT summary FROM kb_entry WHERE id = @entry", new { entry = Guid.Parse(_entry) });
    }
}
