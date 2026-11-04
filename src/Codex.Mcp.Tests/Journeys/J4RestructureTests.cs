using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J4 · Outline and restructure, as the agent does it (#16 J4, #20 edit_outline): move Ch 7 ahead of Ch 6 and archive Ch 8 ·
// Sc 1 in one batch. dry_run reports first and writes nothing; the apply renumbers, says what the archive does to the
// Velocity Trap use, and the Plan screen's outline agrees. Undo puts it back.
[TestFixture]
[Category("J4")]
[CancelAfter(120000)]
public class J4RestructureTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly object[] Ops =
    [
        new { op = "move", item = "7", before = "6" },
        new { op = "archive", item = "8.1" },
    ];

    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private string _undo = "";

    [Test]
    [Order(1)]
    public async Task EditOutline_WhenDryRunWithDisplayNumbers_ShouldReportTheRenumberingAndWriteNothing()
    {
        var result = await Client.OkAsync("edit_outline", new { book = Book, ops = Ops, dry_run = true });

        result.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        result.GetProperty("renumbered").EnumerateArray().Select(r => (r.GetProperty("from").GetString(), r.GetProperty("to").GetString()))
            .ShouldBe([("Ch 6", "Ch 7"), ("Ch 7", "Ch 6")], ignoreOrder: true);
        result.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await PlanChapters()).Single(c => c.Id == SeedIds.Chapters.Ch7).Number.ShouldBe(7);
        (await _server.ScalarAsync<bool>("SELECT archived_at IS NULL FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch8Sc1 })).ShouldBeTrue();
    }

    [Test]
    [Order(2)]
    public async Task EditOutline_WhenApplied_ShouldReturnTheEnvelopeAndTheRenumberingSideEffects()
    {
        var result = await Client.OkAsync("edit_outline", new { book = Book, ops = Ops });

        _undo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("results").GetArrayLength().ShouldBe(2);
        result.GetProperty("affected").GetArrayLength().ShouldBeGreaterThan(0);
        var effects = result.GetProperty("side_effects").EnumerateArray().Select(e => e.GetString()!).ToList();
        effects.ShouldContain(e => e.Contains("Shipping Small", StringComparison.Ordinal));
        (await _server.ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch8Sc1 })).ShouldBeTrue();
    }

    [Test]
    [Order(3)]
    public async Task EditOutline_WhenApplied_ShouldShowTheNewOrderAndNoArchivedSceneOnThePlanScreen()
    {
        var chapters = await PlanChapters();

        chapters.Single(c => c.Id == SeedIds.Chapters.Ch7).Number.ShouldBe(6);
        chapters.Single(c => c.Id == SeedIds.Chapters.Ch6).Number.ShouldBe(7);
        var outline = await _server.ApiAsync($"/api/books/{Book}/outline");
        outline.ToString().ShouldNotContain(SeedIds.Scenes.Ch8Sc1.ToString());
    }

    [Test]
    [Order(4)]
    public async Task EditOutline_WhenTheArchivedSceneQuotedASource_ShouldMoveItOutOfRiskOnTheReferencesScreen()
    {
        // J4 "Watch for": the Velocity Trap quote drops under the 300-word limit. The tool's side_effects do not say so
        // (only the renumbering): a gap recorded in the slice report; the References screen's data is the check here.
        var sources = await _server.ApiAsync($"/api/books/{Book}/sources");

        var trap = sources.ToString();
        trap.ShouldContain("Velocity Trap");
        (await _server.ScalarAsync<int>("SELECT quoted_words FROM reference_clearance WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap }))
            .ShouldBe(214);
        (await _server.ScalarAsync<string>("SELECT status FROM reference_clearance WHERE reference_id = @id", new { id = SeedIds.References.VelocityTrap }))
            .ShouldBe("needs_check");
    }

    [Test]
    [Order(5)]
    public async Task EditOutline_WhenAnOpNamesAMissingScene_ShouldFailNotFoundAndChangeNothing()
    {
        var error = await Client.ErrorAsync("edit_outline", new { book = Book, ops = new object[] { new { op = "archive", item = "42.1" } } });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await PlanChapters()).Single(c => c.Id == SeedIds.Chapters.Ch7).Number.ShouldBe(6);
    }

    [Test]
    [Order(6)]
    public async Task EditOutline_WhenTheOpIsUnknown_ShouldFailInvalid()
    {
        var error = await Client.ErrorAsync("edit_outline", new { book = Book, ops = new object[] { new { op = "shuffle", item = "6" } } });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(7)]
    public async Task Undo_WhenTheBatchIsUndone_ShouldPutTheOutlineBack()
    {
        await Client.OkAsync("undo", new { undo_id = _undo });

        var chapters = await PlanChapters();
        chapters.Single(c => c.Id == SeedIds.Chapters.Ch7).Number.ShouldBe(7);
        (await _server.ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM scene WHERE id = @id", new { id = SeedIds.Scenes.Ch8Sc1 })).ShouldBeFalse();
    }

    private async Task<List<(Guid Id, int Number)>> PlanChapters()
    {
        var outline = await _server.ApiAsync($"/api/books/{Book}/outline");
        return outline.GetProperty("acts").EnumerateArray().SelectMany(a => a.GetProperty("chapters").EnumerateArray())
            .Select(c => (c.GetProperty("id").GetGuid(), c.GetProperty("number").GetInt32())).ToList();
    }
}
