using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J8 · Find everywhere an entry is used, as the agent does it (#16 J8, #20 find_kb_usages, get_kb_entry): Goodhart's Law
// is used in Ch 4 · Sc 1 and Ch 5 · Sc 2, each with the kinds of use and a Write URL that carries the highlight. Read-only,
// so the HTTP API's own list of uses must agree and nothing may change.
[TestFixture]
[Category("J8")]
[CancelAfter(120000)]
public class J8WhereUsedTests
{
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task FindKbUsages_WhenTheEntryIsGoodhartsLaw_ShouldListBothScenesWithKindsAndHighlightUrls()
    {
        var body = await Client.OkAsync("find_kb_usages", new { entry = "Goodhart's Law" });

        var uses = body.GetProperty("uses").EnumerateArray().ToList();
        uses.Select(u => u.GetProperty("item").GetProperty("number").GetString()).ShouldBe(["4.1", "5.2"]);
        uses.ShouldAllBe(u => u.GetProperty("kinds").GetArrayLength() > 0);
        uses[0].GetProperty("item").GetProperty("open_url").GetString()
            .ShouldBe($"/books/{SeedIds.Books.ProductEngineering}/write#scene=4.1&kb={SeedIds.KbEntries.GoodhartsLaw}");
        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.KbEntries.GoodhartsLaw);
    }

    [Test]
    [Order(2)]
    public async Task FindKbUsages_WhenComparedWithTheSidebar_ShouldAgreeOnWhichScenesUseTheEntry()
    {
        var mcp = await Client.OkAsync("find_kb_usages", new { entry = SeedIds.KbEntries.GoodhartsLaw.ToString() });

        var http = await _server.ApiAsync($"/api/kb/entries/{SeedIds.KbEntries.GoodhartsLaw}");

        http.GetProperty("usedIn").EnumerateArray().Select(u => u.GetProperty("number").GetString())
            .ShouldBe(mcp.GetProperty("uses").EnumerateArray().Select(u => u.GetProperty("item").GetProperty("number").GetString()), ignoreOrder: true);
    }

    [Test]
    [Order(3)]
    public async Task GetKbEntry_WhenOpenedByAlias_ShouldAlsoListTheAdditionTheWriteScreenShows()
    {
        var body = await Client.OkAsync("get_kb_entry", new { entry = "goodhart" });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.KbEntries.GoodhartsLaw);
        body.GetProperty("additions").EnumerateArray().Single().GetProperty("scene").GetProperty("number").GetString().ShouldBe("4.1");
    }

    [Test]
    [Order(4)]
    public async Task FindKbUsages_WhenTheEntryDoesNotExist_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("find_kb_usages", new { entry = "No Such Law" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(5)]
    public async Task FindKbUsages_WhenTheEntryIsAnEmptyString_ShouldBeInvalidOrNotFoundAndWriteNothing()
    {
        var error = await Client.ErrorAsync("find_kb_usages", new { entry = "  " });

        error.GetProperty("code").GetString().ShouldBeOneOf("invalid", "not_found");
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(0);
    }
}
