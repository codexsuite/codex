using Codex.Application.Identity;
using Codex.Application.Mcp;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests;

// preview_layout and export_for_printer (#20 section G) over the Print services. The print toolchain (pandoc, Tectonic) is
// CI-only, so these go as far as the queued job and the refusals: preflight, a blocked queue, a queued export and proof read
// with get_job. The stdio process is given the repo's templates/ (CODEX_TEMPLATES_ROOT), which preflight needs. One scenario
// on its own clone; the web host on the same clone also runs the worker, so a queued job may already have settled when read.
[TestFixture]
[CancelAfter(180000)]
public class McpPrintToolsTests
{
    private static readonly string[] JobStates = ["queued", "running", "done", "failed"];
    private static readonly string Risky = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartWithTemplatesAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ExportForPrinter_WhenPreflightingABookWithCopyrightRisks_ShouldListThemAsBlocksAndMatchTheHttpApi()
    {
        var body = await Client.OkAsync("export_for_printer", new { book = Risky, mode = "preflight" });

        body.GetProperty("verdict").GetString().ShouldBe("blocked");
        body.GetProperty("can_queue").GetBoolean().ShouldBeFalse();
        var risks = body.GetProperty("blocks").EnumerateArray().Where(b => b.GetProperty("kind").GetString() == "copyright_risk").ToList();
        risks.Count.ShouldBeGreaterThan(0);
        risks[0].GetProperty("source").GetProperty("open_url").GetString()!.ShouldContain($"/books/{SeedIds.Books.ProductEngineering}/references#");
        body.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/layout");
        var http = await _server.PostApiAsync($"/api/books/{SeedIds.Books.ProductEngineering}/export/preflight", new { });
        http.GetProperty("blocks").GetArrayLength().ShouldBe(body.GetProperty("blocks").GetArrayLength());
        http.GetProperty("warnings").GetArrayLength().ShouldBe(body.GetProperty("warnings").GetArrayLength());
        (await ExportRowsAsync(SeedIds.Books.ProductEngineering)).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task ExportForPrinter_WhenQueuingABookWithCopyrightRisks_ShouldBeBlockedAndQueueNothing()
    {
        var error = await Client.ErrorAsync("export_for_printer", new { book = Risky, mode = "queue" });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        (await ExportRowsAsync(SeedIds.Books.ProductEngineering)).ShouldBe(0);
    }

    [TestCase("later")]
    [TestCase("")]
    public async Task ExportForPrinter_WhenTheModeIsUnknown_ShouldBeInvalid(string mode)
    {
        var error = await Client.ErrorAsync("export_for_printer", new { book = Risky, mode });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("mode");
    }

    [Test]
    public async Task ExportForPrinter_WhenTheBleedIsNotOneOfTheDialogsChoices_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("export_for_printer", new { book = Risky, mode = "preflight", bleed = "5in" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(3)]
    public async Task ExportForPrinter_WhenTheBookHasNoBlocks_ShouldQueueAJobThatGetJobReads()
    {
        var undertow = SeedIds.Books.Undertow;
        var preflight = await Client.OkAsync("export_for_printer", new { book = undertow.ToString(), mode = "preflight", trim = "6x9" });
        preflight.GetProperty("verdict").GetString().ShouldBe("ready", preflight.ToString());

        var queued = await Client.OkAsync("export_for_printer", new { book = undertow.ToString(), mode = "queue", trim = "6x9", bleed = "0.125in", crop_marks = true });

        var id = queued.GetProperty("job_id").GetGuid();
        queued.GetProperty("state").GetString().ShouldBe("queued");
        queued.GetProperty("item").GetProperty("kind").GetString().ShouldBe("export");
        queued.GetProperty("affected").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(id);
        var job = await Client.OkAsync("get_job", new { job_id = id });
        job.GetProperty("kind").GetString().ShouldBe("print");
        JobStates.ShouldContain(job.GetProperty("state").GetString()!);
        var api = await _server.ApiAsync($"/api/exports/{id}");
        api.GetProperty("trim").GetString().ShouldBe("6x9");
        api.GetProperty("bleed").GetString().ShouldBe("0.125in");
        api.GetProperty("cropMarks").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task PreviewLayout_WhenProofingAChapter_ShouldQueueAProofThatGetJobReads()
    {
        var body = await Client.OkAsync("preview_layout", new { book = Risky, scope = "5", kind = "proof" });

        var id = body.GetProperty("job_id").GetGuid();
        body.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/layout#chapter=5");
        var job = await Client.OkAsync("get_job", new { job_id = id });
        job.GetProperty("kind").GetString().ShouldBe("proof");
        (await _server.ApiAsync($"/api/proofs/{id}")).GetProperty("chapterId").GetGuid().ShouldBe(SeedIds.Chapters.Ch5);
        var again = await Client.OkAsync("preview_layout", new { book = Risky, scope = "5.1", kind = "proof" });
        again.GetProperty("job_id").GetGuid().ShouldBe(id);
        again.GetProperty("cached").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task PreviewLayout_WhenQuickPreviewingAChapter_ShouldEstimateAndLinkToLayoutOrSayPandocIsMissing()
    {
        var result = await Client.CallToolAsync("preview_layout", new Dictionary<string, object?> { ["book"] = Risky, ["scope"] = "5" }, cancellationToken: McpCalls.Token);

        var body = System.Text.Json.JsonDocument.Parse(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text).RootElement;
        if (result.IsError == true)
        {
            // This host has no pandoc (the print toolchain is CI-only); the preview needs it.
            body.GetProperty("error").GetProperty("code").GetString().ShouldBe("not_configured");
            return;
        }
        body.GetProperty("estimated_pages").GetInt32().ShouldBeGreaterThan(0);
        body.GetProperty("template").GetString().ShouldNotBeNullOrEmpty();
        body.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/layout#chapter=5");
    }

    [Test]
    public async Task PreviewLayout_WhenProofingWithoutAChapter_ShouldBeInvalid()
    {
        (await Client.ErrorAsync("preview_layout", new { book = Risky, kind = "proof" })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("preview_layout", new { book = Risky, scope = "act:1", kind = "proof" })).GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    public async Task PreviewLayout_WhenTheKindOrScopeIsWrong_ShouldFailAsInvalidOrNotFound()
    {
        (await Client.ErrorAsync("preview_layout", new { book = Risky, kind = "poster" })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("preview_layout", new { book = Risky, scope = "act:1" })).GetProperty("code").GetString().ShouldBe("invalid");
        (await Client.ErrorAsync("preview_layout", new { book = Risky, scope = "99" })).GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public void RequireScope_WhenTheGrantLacksTheScope_ShouldBeForbiddenNamingIt()
    {
        var readOnly = new McpGrant(Guid.NewGuid(), Guid.NewGuid(), "read-only", [ApiTokenScopes.Read]);

        var e = Should.Throw<McpToolException>(() => McpScopes.RequireScope(readOnly, "preview_layout", ApiTokenScopes.Write));

        (e.Code, e.Field).ShouldBe(("forbidden", ApiTokenScopes.Write));
        McpScopes.ForTool["export_for_printer"].ShouldBe(ApiTokenScopes.Read);
        McpScopes.ForTool["preview_layout"].ShouldBe(ApiTokenScopes.Read);
    }

    private Task<int> ExportRowsAsync(Guid book) =>
        _server.ScalarAsync<int>("SELECT count(*)::int FROM export_job WHERE book_id = @book AND kind = 'print'", new { book });
}
