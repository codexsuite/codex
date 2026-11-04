using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J12 · Get ready to print, as the agent does it (#16 J12, #20 get_clearance, update_book, export_references, preview_layout,
// export_for_printer, get_job): get_clearance says the print gate is shut; preflight lists the copyright risks as blocks and
// queue is refused; the citation style goes to APA; the bibliography and the permissions log are exported; a proof of Ch 5 is
// queued and read with get_job; then the export is queued and read with get_job. The HTTP API the Layout screen reads must agree.
// Deviations: steps 2 (J10 and J11) are covered by those journeys, so the three risks are still open here and Product
// Engineering cannot be queued: its queue is refused, and the successful queue and get_job run on Undertow, which has none.
// Step 6's Quick preview needs pandoc (CI-only), so the preview here is the proof, which stops at the queued job (the worker
// compiles it); preview_layout kind "quick" is covered in McpPrintToolsTests where the host has pandoc. A queued job may
// already have settled when read (the web host on the clone runs the worker), so get_job is checked for its kind and a
// known state, not one state.
[TestFixture]
[Category("J12")]
[CancelAfter(180000)]
public class J12GetReadyToPrintTests
{
    private static readonly string[] JobStates = ["queued", "running", "done", "failed"];
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartWithTemplatesAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task GetClearance_WhenThePrintGateIsLookedAt_ShouldSayPrintIsBlockedAsTheReferencesTabDoes()
    {
        var summary = (await Client.OkAsync("get_clearance", new { book = Book })).GetProperty("summary");

        summary.GetProperty("print_blocked").GetBoolean().ShouldBeTrue();
        var http = await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/references/summary");
        summary.GetProperty("risks").GetInt32().ShouldBe(http.GetProperty("risks").GetInt32());
    }

    [Test]
    [Order(2)]
    public async Task ExportForPrinter_WhenPreflightedAndQueuedWithRisksOpen_ShouldListThemAndRefuseTheQueue()
    {
        var preflight = await Client.OkAsync("export_for_printer", new { book = Book, mode = "preflight", trim = "6x9", bleed = "0.125in", image_quality = "print" });

        preflight.GetProperty("verdict").GetString().ShouldBe("blocked");
        preflight.GetProperty("blocks").EnumerateArray().Count(b => b.GetProperty("kind").GetString() == "copyright_risk").ShouldBeGreaterThan(0);
        var error = await Client.ErrorAsync("export_for_printer", new { book = Book, mode = "queue", trim = "6x9", bleed = "0.125in" });
        error.GetProperty("code").GetString().ShouldBe("blocked");
        (await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/exports")).GetProperty("exports").GetArrayLength().ShouldBe(0);
    }

    [Test]
    [Order(3)]
    public async Task UpdateBook_WhenTheCitationStyleGoesToApa_ShouldShowInThePublicationSettings()
    {
        var body = await Client.OkAsync("update_book", new { book = Book, publication = new { citation_style = "apa" } });

        body.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
        var http = await _server.ApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/publication-settings");
        http.GetProperty("style").GetProperty("value").GetString().ShouldBe("apa");
    }

    [Test]
    [Order(4)]
    public async Task ExportReferences_WhenThePermissionsLogAndCslJsonAreExported_ShouldReturnBoth()
    {
        var log = await Client.OkAsync("export_references", new { book = Book, format = "permissions_log" });
        var csl = await Client.OkAsync("export_references", new { book = Book, format = "csl_json" });

        log.GetProperty("entries").GetInt32().ShouldBeGreaterThan(0);
        csl.GetProperty("content").GetString().ShouldStartWith("[");
    }

    [Test]
    [Order(5)]
    public async Task PreviewLayout_WhenAChapterIsProofed_ShouldQueueAProofGetJobReadsAndTheLayoutLinkOpens()
    {
        var proof = await Client.OkAsync("preview_layout", new { book = Book, scope = "5", kind = "proof" });

        var id = proof.GetProperty("job_id").GetGuid();
        proof.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/layout#chapter=5");
        proof.GetProperty("estimated_pages").GetInt32().ShouldBeGreaterThan(0);
        var job = await Client.OkAsync("get_job", new { job_id = id });
        job.GetProperty("kind").GetString().ShouldBe("proof");
        JobStates.ShouldContain(job.GetProperty("state").GetString()!);
        (await _server.ApiAsync($"/api/proofs/{id}")).GetProperty("chapterId").GetGuid().ShouldBe(SeedIds.Chapters.Ch5);
    }

    [Test]
    [Order(6)]
    public async Task ExportForPrinter_WhenABookWithNoRisksIsQueued_ShouldReturnAJobGetJobAndTheExportsListAgreeOn()
    {
        var book = SeedIds.Books.Undertow;
        var preflight = await Client.OkAsync("export_for_printer", new { book = book.ToString(), mode = "preflight", trim = "6x9", bleed = "0.125in" });
        preflight.GetProperty("can_queue").GetBoolean().ShouldBeTrue();

        var queued = await Client.OkAsync("export_for_printer", new { book = book.ToString(), mode = "queue", trim = "6x9", bleed = "0.125in" });

        var id = queued.GetProperty("job_id").GetGuid();
        queued.GetProperty("side_effects").EnumerateArray().Single().GetString()!.ShouldContain("get_job");
        var job = await Client.OkAsync("get_job", new { job_id = id });
        job.GetProperty("job_id").GetGuid().ShouldBe(id);
        job.GetProperty("kind").GetString().ShouldBe("print");
        JobStates.ShouldContain(job.GetProperty("state").GetString()!);
        var list = await _server.ApiAsync($"/api/books/{book}/exports");
        list.GetProperty("exports").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ShouldContain(id);
    }
}
