using System.Text;
using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;

namespace Codex.Mcp.Tests;

// list_kb_entries (#77) over stdio against a clone of the seed. Nothing here writes, so the tests share one server.
[TestFixture]
[CancelAfter(60000)]
public class McpListKbEntriesTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task ListKbEntries_WhenBookHasNoSeries_ShouldListItsTwelveEntriesInScreenOrder()
    {
        var body = await _server.Client.OkAsync("list_kb_entries", new { book = ProductEngineering });

        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        entries.Select(e => e.GetProperty("name").GetString()).ShouldBe(
        [
            "Signs of Bad Scrum", "Feature Factory", "Why We Stopped Estimating", "Conway's Law", "Goodhart's Law", "Brooks's Law",
            "The Payments Rewrite", "Team Atlas Offsite", "DORA Four Keys", "Agile Manifesto (2001)", "Kent Beck", "Marty Cagan",
        ]);
        body.GetProperty("total").GetInt32().ShouldBe(12);
        body.TryGetProperty("next_cursor", out var next).ShouldBeFalse(next.ToString());
        var goodhart = entries[4];
        goodhart.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.KbEntries.GoodhartsLaw);
        goodhart.GetProperty("item").GetProperty("kind").GetString().ShouldBe("kb_entry");
        goodhart.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{ProductEngineering}/plan#entry={SeedIds.KbEntries.GoodhartsLaw}");
        goodhart.GetProperty("category").GetString().ShouldBe("Laws");
        goodhart.GetProperty("scope").GetString().ShouldBe("book");
        goodhart.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()).ShouldBe(["Goodhart"]);
        goodhart.GetProperty("always_in_context").ValueKind.ShouldBeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    [Test]
    public async Task ListKbEntries_WhenEntryIsUsed_ShouldCountDistinctScenesAsGetKbEntryDoes()
    {
        var list = await _server.Client.OkAsync("list_kb_entries", new { book = ProductEngineering, category = "Laws" });
        var detail = await _server.Client.OkAsync("get_kb_entry", new { entry = "Goodhart" });

        var goodhart = list.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "Goodhart's Law");
        goodhart.GetProperty("used_in_scenes").GetInt32().ShouldBe(detail.GetProperty("used_in_scenes").GetInt32());
        goodhart.GetProperty("used_in_scenes").GetInt32().ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task ListKbEntries_WhenCategoryGivenInAnyCase_ShouldListOnlyThatCategory()
    {
        var body = await _server.Client.OkAsync("list_kb_entries", new { book = ProductEngineering, category = "lAWS" });

        body.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString())
            .ShouldBe(["Conway's Law", "Goodhart's Law", "Brooks's Law"]);
        body.GetProperty("total").GetInt32().ShouldBe(3);
    }

    [Test]
    public async Task ListKbEntries_WhenCategoryIsUnknown_ShouldFailNotFoundAndListTheCategories()
    {
        var error = await _server.Client.ErrorAsync("list_kb_entries", new { book = ProductEngineering, category = "Monsters" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        error.ToString().ShouldContain("Blog Posts");
        error.ToString().ShouldContain("People");
    }

    [Test]
    public async Task ListKbEntries_WhenLimitIsFive_ShouldPageAcrossThreePagesWithoutRepeats()
    {
        var names = new List<string?>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var body = await _server.Client.OkAsync("list_kb_entries", new { book = ProductEngineering, limit = 5, cursor });
            names.AddRange(body.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
            body.GetProperty("total").GetInt32().ShouldBe(12);
            cursor = body.TryGetProperty("next_cursor", out var next) ? next.GetString() : null;
            pages++;
        }
        while (cursor != null);

        pages.ShouldBe(3);
        names.Count.ShouldBe(12);
        names.Distinct().Count().ShouldBe(12);
        names[0].ShouldBe("Signs of Bad Scrum");
        names[11].ShouldBe("Marty Cagan");
    }

    [Test]
    public async Task ListKbEntries_WhenBookIsInASeries_ShouldIncludeTheSeriesEntriesAsSeriesScope()
    {
        var body = await _server.Client.OkAsync("list_kb_entries", new { book = SeedIds.Books.PlayersGuide.ToString() });

        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        entries.Select(e => e.GetProperty("name").GetString()).ShouldBe(["Gravetouch", "Hollow Prayer", "Ember Ward"]);
        entries.ShouldAllBe(e => e.GetProperty("scope").GetString() == "series" && e.GetProperty("category").GetString() == "Spells");
    }

    [Test]
    public async Task ListKbEntries_WhenCursorIsNotOneWeIssued_ShouldFailInvalid()
    {
        var error = await _server.Client.ErrorAsync("list_kb_entries", new { book = ProductEngineering, cursor = "nonsense" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    public async Task ListKbEntries_WhenCursorIsWellFormedButForeign_ShouldFailInvalid()
    {
        var cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("other:5"));

        var error = await _server.Client.ErrorAsync("list_kb_entries", new { book = ProductEngineering, cursor });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    public async Task ListKbEntries_WhenLimitIsOverTheMaximum_ShouldFailInvalid()
    {
        var error = await _server.Client.ErrorAsync("list_kb_entries", new { book = ProductEngineering, limit = 501 });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }
}
