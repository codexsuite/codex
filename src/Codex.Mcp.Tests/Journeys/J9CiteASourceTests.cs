using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J9 · Cite a source while writing, as the agent does it (#16 J9, #20 cite_source): A, cite Accelerate again in Ch 7 · Sc 1
// (marker 8, Needs check, uses since sign-off); B, cite a book by its ISBN, which matches Lean Software Development (in
// References, Not cited) instead of adding a duplicate, and the markers agree with the HTTP API Write and References read.
// Deviation from the seed: its sources carry no ISBN, so B first gives Lean Software Development 9780321150783 through
// update_source (J7's variant), then cites by that ISBN.
[TestFixture]
[Category("J9")]
[CancelAfter(120000)]
public class J9CiteASourceTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Scene = SeedIds.Scenes.Ch7Sc1.ToString();
    private JourneyServer _server = null!;
    private Guid _useA;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<string> PassageAsync(int skip)
    {
        var (body, _) = await _server.ProseAsync(SeedIds.Scenes.Ch7Sc1);
        var paragraph = body.Split("\n\n").Where(p => !p.StartsWith("::") && !p.Contains("](ref:") && p.Split(' ').Length > 14).ElementAt(skip);
        return string.Join(' ', paragraph.Split(' ').Take(6));
    }

    private async Task<int> MarkerAsync(Guid source) =>
        (await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/sources")).EnumerateArray()
            .Single(s => s.GetProperty("id").GetGuid() == source).GetProperty("number").GetInt32();

    [Test]
    [Order(1)]
    public async Task CiteSource_WhenAccelerateIsCitedAgainInCh7_ShouldGetMarker8AndWarnAboutUsesSinceSignOff()
    {
        var (_, version) = await _server.ProseAsync(SeedIds.Scenes.Ch7Sc1);

        var body = await Client.OkAsync("cite_source",
            new { scene = "7.1", book = Book, passage = new { text = await PassageAsync(0) }, source = "Accelerate", kind = "paraphrase", expected_version = version });

        body.GetProperty("marker").GetInt32().ShouldBe(8);
        body.GetProperty("status").GetString().ShouldBe("needs_check");
        body.GetProperty("kind").GetString().ShouldBe("paraphrase");
        body.GetProperty("scene").GetProperty("number").GetString().ShouldBe("7.1");
        body.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/references#{SeedIds.References.Accelerate}");
        body.GetProperty("side_effects").EnumerateArray().Select(e => e.GetString()).ShouldContain(e => e!.Contains("since sign-off"));
        body.GetProperty("undo_id").GetString().ShouldStartWith("u_");
        _useA = body.GetProperty("use").GetGuid();
    }

    [Test]
    [Order(2)]
    public async Task CiteSource_WhenAIsDone_ShouldShowTheMarkerInWriteAndTheUseInReferences()
    {
        var (body, _) = await _server.ProseAsync(SeedIds.Scenes.Ch7Sc1);
        body.ShouldContain($"](ref:{_useA})");

        var markers = await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/markers");
        markers.EnumerateArray().Single(m => m.GetProperty("useId").GetGuid() == _useA).GetProperty("number").GetInt32().ShouldBe(8);
        var detail = await _server.ApiAsync($"/api/references/{SeedIds.References.Accelerate}");
        detail.GetProperty("uses").EnumerateArray().Count(u => u.GetProperty("newSinceSignOff").GetBoolean()).ShouldBe(2);
        detail.GetProperty("history").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
    }

    [Test]
    [Order(3)]
    public async Task CiteSource_WhenAnIsbnMatchesALibrarySourceThatIsNotCited_ShouldUseItAndShiftTheMarkersAfterIt()
    {
        await Client.OkAsync("update_source", new { source = "Lean Software Development", book = Book, fields = new { isbn = "9780321150783" } });
        var (_, version) = await _server.ProseAsync(SeedIds.Scenes.Ch7Sc1);
        var sources = await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE archived_at IS NULL");
        var markersBefore = (await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/sources")).EnumerateArray()
            .Where(s => s.GetProperty("number").ValueKind == System.Text.Json.JsonValueKind.Number)
            .ToDictionary(s => s.GetProperty("id").GetGuid(), s => s.GetProperty("number").GetInt32());

        var body = await Client.OkAsync("cite_source", new
        {
            scene = "7.1", book = Book, passage = new { text = await PassageAsync(1) }, source = new { isbn = "978-0-321-15078-3" }, kind = "quote",
            locator = "p. 4", expected_version = version,
        });

        body.GetProperty("source_how").GetString().ShouldBe("matched");
        body.GetProperty("source").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.LeanSoftware);
        body.GetProperty("status_before").GetString().ShouldBe("not_cited");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE archived_at IS NULL")).ShouldBe(sources);
        var number = await MarkerAsync(SeedIds.References.LeanSoftware);
        body.GetProperty("marker").GetInt32().ShouldBe(number);
        var markersAfter = (await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/sources")).EnumerateArray()
            .Where(s => s.GetProperty("number").ValueKind == System.Text.Json.JsonValueKind.Number)
            .ToDictionary(s => s.GetProperty("id").GetGuid(), s => s.GetProperty("number").GetInt32());
        number.ShouldBe(9);
        // Every source numbered at or after Lean's marker moved by exactly one; the ones before it did not move.
        foreach (var (id, was) in markersBefore)
        {
            markersAfter[id].ShouldBe(was >= number ? was + 1 : was);
        }
        markersBefore.Count(m => m.Value >= number).ShouldBe(body.GetProperty("markers_shifted").GetInt32());
        // Write's markers (/markers) and References (/sources) number every source the same.
        var written = (await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/markers")).EnumerateArray().ToList();
        written.ShouldAllBe(m => markersAfter[m.GetProperty("sourceId").GetGuid()] == m.GetProperty("number").GetInt32());
        written.Single(m => m.GetProperty("useId").GetGuid() == body.GetProperty("use").GetGuid()).GetProperty("number").GetInt32().ShouldBe(number);
    }

    [Test]
    [Order(4)]
    public async Task CiteSource_WhenTheSourceIsNotInTheLibraryAndNothingIdentifiesIt_ShouldBeNotFound()
    {
        var (_, version) = await _server.ProseAsync(SeedIds.Scenes.Ch7Sc1);

        var error = await Client.ErrorAsync("cite_source",
            new { scene = "7.1", book = Book, passage = new { text = "x" }, source = "Domain-Driven Design", expected_version = version });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(5)]
    public async Task CiteSource_WhenTheSceneHasMovedOnSinceItWasRead_ShouldBeAConflictAndLeaveTheMarkerCountAlone()
    {
        var (_, version) = await _server.ProseAsync(SeedIds.Scenes.Ch7Sc1);
        var uses = await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL");

        var error = await Client.ErrorAsync("cite_source",
            new { scene = Scene, passage = new { text = await PassageAsync(2) }, source = "Accelerate", expected_version = version - 1 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL")).ShouldBe(uses);
    }
}
