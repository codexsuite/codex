using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests;

// get_clearance and export_references over stdio on the Product Engineering seed (4 of 11 cited sources cleared, 3 risks,
// 2 unflagged quotes): the summary strip, each source's next action in the References panel's words, one source in
// detail, the filters and refusals, and the exports (cited sources only unless asked). Read-only, so nothing is written.
[TestFixture]
[CancelAfter(60000)]
public class McpClearanceReadTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task GetClearance_WhenAskedForTheBook_ShouldGiveTheStripTheSourcesAndTheInbox()
    {
        var body = await Client.OkAsync("get_clearance", new { book = Book });

        var summary = body.GetProperty("summary");
        summary.GetProperty("cited").GetInt32().ShouldBe(11);
        summary.GetProperty("cleared").GetInt32().ShouldBe(4);
        summary.GetProperty("risks").GetInt32().ShouldBe(3);
        summary.GetProperty("print_blocked").GetBoolean().ShouldBeTrue();
        summary.GetProperty("unflagged_quotes").GetInt32().ShouldBe(2);
        var sources = body.GetProperty("sources").EnumerateArray().ToDictionary(s => s.GetProperty("title").GetString()!);
        sources["The Velocity Trap"].GetProperty("marker").GetInt32().ShouldBe(4);
        sources["The Velocity Trap"].GetProperty("reason").GetString().ShouldBe("342 of 300 words, no request");
        sources["The Velocity Trap"].GetProperty("next_action").GetString().ShouldBe("reduce_quoted_words");
        sources["Team Topologies"].GetProperty("next_action").GetString().ShouldBe("chase_permission");
        sources["Ship It Friday"].GetProperty("next_action").GetString().ShouldBe("cut_or_request");
        sources["Accelerate"].GetProperty("next_action").GetString().ShouldBe("review_new_use");
        sources["Lean Software Development"].GetProperty("status").GetString().ShouldBe("not_cited");
        sources["The Velocity Trap"].GetProperty("source").GetProperty("open_url").GetString()
            .ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/references#{SeedIds.References.VelocityTrap}");
        body.GetProperty("flags").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())
            .ShouldBe([SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote, SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch], ignoreOrder: true);
    }

    [Test]
    public async Task GetClearance_WhenFilteredToRisks_ShouldListOnlyTheThreeRisks()
    {
        var body = await Client.OkAsync("get_clearance", new { book = Book, status = new[] { "risk" }, include = Array.Empty<string>() });

        body.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("marker").GetInt32()).ShouldBe([4, 9, 11], ignoreOrder: true);
        body.TryGetProperty("flags", out _).ShouldBeFalse();
    }

    [Test]
    public async Task GetClearance_WhenOneSourceIsNamedByMarker_ShouldGiveItsUsesChecksAndHistory()
    {
        var body = await Client.OkAsync("get_clearance", new { book = Book, source = "#4" });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.VelocityTrap);
        var detail = body.GetProperty("detail");
        detail.GetProperty("uses").GetArrayLength().ShouldBe(2);
        detail.GetProperty("uses").EnumerateArray().First().GetProperty("scene").GetProperty("number").GetString().ShouldBe("4.1");
        detail.GetProperty("checks").GetArrayLength().ShouldBeGreaterThan(0);
        detail.GetProperty("history").ValueKind.ShouldBe(JsonValueKind.Array);
        detail.GetProperty("quoted_words").GetInt32().ShouldBe(342);
        body.GetProperty("focus").GetProperty("next_action").GetString().ShouldBe("reduce_quoted_words");
    }

    [Test]
    public async Task GetClearance_WhenTheSourceIsNamedByTitleOrAnId_ShouldFindTheSameSource()
    {
        var byTitle = await Client.OkAsync("get_clearance", new { book = Book, source = "team topologies", include = new[] { "permission" } });
        var byId = await Client.OkAsync("get_clearance", new { source = SeedIds.References.TeamTopologies.ToString() });

        byTitle.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.TeamTopologies);
        byId.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.TeamTopologies);
        byTitle.GetProperty("detail").GetProperty("permission").GetProperty("status").GetString().ShouldBe("sent");
    }

    [Test]
    public async Task GetClearance_WhenTheSourceOrStatusIsUnknown_ShouldBeNotFoundOrInvalid()
    {
        (await Client.ErrorAsync("get_clearance", new { book = Book, source = "No Such Book" })).GetProperty("code").GetString().ShouldBe("not_found");
        (await Client.ErrorAsync("get_clearance", new { book = Book, source = "#99" })).GetProperty("code").GetString().ShouldBe("not_found");
        var invalid = await Client.ErrorAsync("get_clearance", new { book = Book, status = new[] { "fine" } });
        invalid.GetProperty("code").GetString().ShouldBe("invalid");
        invalid.GetProperty("field").GetString().ShouldBe("status");
    }

    [Test]
    public async Task ExportReferences_WhenUncitedAreLeftOut_ShouldListElevenSourcesWithoutLeanSoftware()
    {
        var bibtex = await Client.OkAsync("export_references", new { book = Book, format = "bibtex" });
        var all = await Client.OkAsync("export_references", new { book = Book, format = "bibtex", include_uncited = true });

        bibtex.GetProperty("entries").GetInt32().ShouldBe(11);
        bibtex.GetProperty("content").GetString()!.ShouldNotContain("Lean Software");
        all.GetProperty("entries").GetInt32().ShouldBe(12);
        all.GetProperty("content").GetString()!.ShouldContain("Lean Software");
    }

    [Test]
    public async Task ExportReferences_WhenOtherFormatsAreAsked_ShouldReturnThemInline()
    {
        var csl = await Client.OkAsync("export_references", new { book = Book, format = "csl_json" });
        var ris = await Client.OkAsync("export_references", new { book = Book, format = "ris" });
        var log = await Client.OkAsync("export_references", new { book = Book, format = "permissions_log" });
        var list = await Client.OkAsync("export_references", new { book = Book });

        JsonDocument.Parse(csl.GetProperty("content").GetString()!).RootElement.GetArrayLength().ShouldBe(11);
        ris.GetProperty("content").GetString()!.ShouldContain("TY  -");
        log.GetProperty("content").GetString()!.ShouldContain("Team Topologies");
        list.GetProperty("format").GetString().ShouldBe("bibliography");
        list.GetProperty("style_applied").GetBoolean().ShouldBeFalse();
        list.GetProperty("content").GetString()!.ShouldContain("The Velocity Trap");
        list.GetProperty("content").GetString()!.Split('\n').Length.ShouldBe(11);
    }

    [Test]
    public async Task ExportReferences_WhenTheFormatIsUnknown_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("export_references", new { book = Book, format = "endnote" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("format");
    }
}
