using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J6 · Fill an empty scene, as the agent does it (#16 J6, #20 edit_outline update, upsert_beat) on Ch 6 · Sc 2: a summary
// with an @[…] mention of a Knowledge base entry, the label Needs research, then a beat in the empty scene. An unknown mention
// is refused and writes nothing.
[TestFixture]
[Category("J6")]
[CancelAfter(120000)]
public class J6FillEmptySceneTests
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
    public async Task EditOutline_WhenTheSummaryMentionsAnUnknownEntry_ShouldFailAndWriteNothing()
    {
        var before = await _server.ScalarAsync<string>("SELECT summary FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch6Sc2 });

        var error = await Client.ErrorAsync("edit_outline",
            new { book = Book, ops = new[] { new { op = "update", item = "6.2", summary = "What @[Nobody Known] taught us." } } });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await _server.ScalarAsync<string>("SELECT summary FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch6Sc2 })).ShouldBe(before);
    }

    [Test]
    [Order(2)]
    public async Task EditOutline_WhenGivenASummaryWithAMentionAndALabel_ShouldShowThemOnThePlanCard()
    {
        var result = await Client.OkAsync("edit_outline", new
        {
            book = Book,
            ops = new[]
            {
                new { op = "update", item = "6.2", summary = "What the second round of interviews taught us about @[Goodhart's Law].", add_labels = new[] { "Needs research" } },
            },
        });

        result.GetProperty("results").GetArrayLength().ShouldBe(1);
        result.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
        var outline = await _server.ApiAsync($"/api/books/{Book}/outline");
        var scene = outline.GetProperty("acts").EnumerateArray()
            .SelectMany(a => a.GetProperty("chapters").EnumerateArray())
            .SelectMany(c => c.GetProperty("scenes").EnumerateArray())
            .Single(s => s.GetProperty("number").GetString() == "6.2");
        scene.GetProperty("summary").GetString()!.ShouldContain($"kb:{SeedIds.KbEntries.GoodhartsLaw}");
        scene.GetProperty("labels").EnumerateArray().Select(l => l.GetProperty("name").GetString()).ShouldBe(["Needs research"]);
    }

    [Test]
    [Order(3)]
    public async Task EditOutline_WhenALabelDoesNotExist_ShouldFailUnlessTheAgentAsksToCreateIt()
    {
        var error = await Client.ErrorAsync("edit_outline",
            new { book = Book, ops = new[] { new { op = "update", item = "6.2", add_labels = new[] { "Brand New Label" } } } });
        error.GetProperty("code").GetString().ShouldBe("invalid");

        var ok = await Client.OkAsync("edit_outline",
            new { book = Book, ops = new[] { new { op = "update", item = "6.2", add_labels = new[] { "Brand New Label" }, create_missing_labels = true } } });
        ok.GetProperty("results").GetArrayLength().ShouldBe(1);
        (await _server.ApiAsync($"/api/books/{Book}/labels")).EnumerateArray().Select(l => l.GetProperty("name").GetString()).ShouldContain("Brand New Label");
    }

    [Test]
    [Order(4)]
    public async Task UpsertBeat_WhenTheSceneIsEmpty_ShouldAddTheBeatLineThatWriteShowsUnderTheSummary()
    {
        var version = (await _server.ProseAsync(SeedIds.Scenes.Ch6Sc2)).Version;

        var result = await Client.OkAsync("upsert_beat",
            new { scene = "6.2", book = Book, at = "end", expected_version = version, instruction = "Open on the interview room.", target_word_count = 400 });

        var beat = result.GetProperty("beat").GetProperty("id").GetString()!;
        result.GetProperty("version").GetInt32().ShouldBe(version + 1);
        (await _server.ApiAsync($"/api/scenes/{SeedIds.Scenes.Ch6Sc2}/prose")).GetProperty("body").GetString()!.ShouldContain($"::beat{{id=\"{beat}\"}}");
    }

    [Test]
    [Order(5)]
    public async Task UpsertBeat_WhenTheVersionIsStale_ShouldConflict()
    {
        var error = await Client.ErrorAsync("upsert_beat",
            new { scene = "6.2", book = Book, at = "end", expected_version = 1, instruction = "x" });

        error.GetProperty("code").GetString().ShouldBe("conflict");
    }
}
