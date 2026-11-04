using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J7 · Add a Knowledge base entry and use it, as the agent does it (#16 J7, #20 upsert_kb_entry, link_kb_entry, upsert_beat):
// create Team Topologies in Research with aliases, link it to Ch 9 · Sc 1, wrap a mention (the agent first writes the plain
// "team types" there, since the seed's scene has none; the dotted alias underline is a UI step), add Conway's Law's
// "Codex addition", and put the entry in a beat's context. A clashing alias is refused; undo archives the entry.
[TestFixture]
[Category("J7")]
[CancelAfter(120000)]
public class J7KbEntryTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Scene = SeedIds.Scenes.Ch9Sc1.ToString();
    private JourneyServer _server = null!;
    private string _entry = "";
    private string _createUndo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task UpsertKbEntry_WhenAnAliasBelongsToAnotherEntry_ShouldBeAmbiguousAndWriteNothing()
    {
        var error = await Client.ErrorAsync("upsert_kb_entry",
            new { book = Book, name = "Team Topologies", category = "Research", aliases = new[] { "Goodhart" } });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM kb_entry WHERE name = 'Team Topologies'")).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task UpsertKbEntry_WhenNew_ShouldCreateItInResearchAndTheEditorReadsItBack()
    {
        var result = await Client.OkAsync("upsert_kb_entry", new
        {
            book = Book, name = "Team Topologies", category = "Research", aliases = new[] { "team types", "interaction modes" },
            summary = "Four fundamental team types and three interaction modes.",
        });

        _entry = result.GetProperty("item").GetProperty("id").GetString()!;
        _createUndo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("created").GetBoolean().ShouldBeTrue();
        result.GetProperty("affected").GetArrayLength().ShouldBeGreaterThan(0);
        var entry = await _server.ApiAsync($"/api/kb/entries/{_entry}");
        entry.GetProperty("categoryName").GetString().ShouldBe("Research");
        entry.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()).ShouldBe(["team types", "interaction modes"], ignoreOrder: true);
    }

    [Test]
    [Order(3)]
    public async Task LinkKbEntry_WhenLinkingTheSceneAndWrappingAMention_ShouldShowTheChipAndTheMentionInProse()
    {
        var version = (await _server.ProseAsync(SeedIds.Scenes.Ch9Sc1)).Version;
        var edit = await Client.OkAsync("edit_prose", new
        {
            scene = Scene, expected_version = version,
            edits = new[] { new { old = "As Brooks put it,", @new = "As Brooks put it about team types," } },
        });
        edit.GetProperty("version").GetInt32().ShouldBe(version + 1);

        var result = await Client.OkAsync("link_kb_entry", new
        {
            entry = _entry,
            links = new object[] { new { @as = "scene", scene = Scene }, new { @as = "mention", scene = Scene, anchor = new { text = "team types" } } },
        });

        result.GetProperty("failed").GetInt32().ShouldBe(0, result.ToString());
        result.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
        (await _server.ApiAsync($"/api/scenes/{SeedIds.Scenes.Ch9Sc1}/prose")).GetProperty("body").GetString()!.ShouldContain($"[team types](kb:{_entry})");
        (await _server.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM scene_kb WHERE scene_id = @s AND kb_entry_id = @e AND archived_at IS NULL)",
            new { s = SeedIds.Scenes.Ch9Sc1, e = Guid.Parse(_entry) })).ShouldBeTrue();
    }

    [Test]
    [Order(4)]
    public async Task LinkKbEntry_WhenAddingAConwaysLawAddition_ShouldWriteTheKbAdditionLine()
    {
        var version = (await _server.ProseAsync(SeedIds.Scenes.Ch9Sc1)).Version;

        var result = await Client.OkAsync("link_kb_entry", new
        {
            entry = SeedIds.KbEntries.ConwaysLaw.ToString(),
            links = new[] { new { @as = "addition", scene = Scene, at = (object)"end", text = "From here the reader knows the inverse Conway manoeuvre.", expected_version = version } },
        });

        result.GetProperty("failed").GetInt32().ShouldBe(0, result.ToString());
        (await _server.ProseAsync(SeedIds.Scenes.Ch9Sc1)).Body.ShouldContain("::kb-addition");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM kb_progression WHERE scene_id = @s AND kb_entry_id = @e",
            new { s = SeedIds.Scenes.Ch9Sc1, e = SeedIds.KbEntries.ConwaysLaw })).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    [Order(5)]
    public async Task UpsertBeat_WhenAddingTheEntryToABeatsContext_ShouldLinkItInBeatKb()
    {
        await Client.OkAsync("upsert_beat", new { beat = SeedIds.Beats.Ch3Sc3BadScrum, book = Book, context_add = new[] { "Team Topologies" } });

        (await _server.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM beat_kb WHERE beat_id = @b AND kb_entry_id = @e AND archived_at IS NULL)",
            new { b = SeedIds.Beats.Ch3Sc3BadScrum, e = Guid.Parse(_entry) })).ShouldBeTrue();
    }

    [Test]
    [Order(6)]
    public async Task FindKbUsages_WhenTheEntryIsUsed_ShouldListTheSceneAsTheEditorDoes()
    {
        var usages = await Client.OkAsync("find_kb_usages", new { entry = _entry });

        usages.GetProperty("uses").EnumerateArray().Select(u => u.GetProperty("item").GetProperty("number").GetString()).ShouldContain("9.1");
        (await _server.ApiAsync($"/api/kb/entries/{_entry}")).GetProperty("usedIn").EnumerateArray()
            .Select(u => u.GetProperty("number").GetString()).ShouldContain("9.1");
    }

    [Test]
    [Order(7)]
    public async Task Undo_WhenTheEntryWasCreated_ShouldArchiveIt()
    {
        await Client.OkAsync("undo", new { undo_id = _createUndo });

        (await _server.ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @e", new { e = Guid.Parse(_entry) })).ShouldBeTrue();
    }
}
