using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J14 · Find a book, as the agent does it (#16 J14, #20 search, list_books, get_outline): search "under" finds Undertow,
// list_books sorted by title is the List view, and "Undertow" opens its Plan. An ambiguous title lists its candidates.
// (The ⌘K palette is the UI's route to the same books.)
[TestFixture]
[Category("J14")]
[CancelAfter(120000)]
public class J14FindBookTests
{
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task Search_WhenTheQueryIsPartOfATitle_ShouldFindUndertowWithItsPlanUrl()
    {
        var body = await Client.OkAsync("search", new { query = "under", types = new[] { "book" } });

        var hit = body.GetProperty("hits").EnumerateArray().Single(h => h.GetProperty("item").GetProperty("id").GetGuid() == SeedIds.Books.Undertow);
        hit.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.Undertow}/plan");
    }

    [Test]
    [Order(2)]
    public async Task ListBooks_WhenSortedByTitle_ShouldMatchTheLibraryShelvesAndHaveUndertowInTheTidewaterSeries()
    {
        var body = await Client.OkAsync("list_books", new { sort = "title" });

        var all = body.GetProperty("series").EnumerateArray().SelectMany(s => s.GetProperty("books").EnumerateArray())
            .Concat(body.GetProperty("standalone").EnumerateArray()).Select(b => b.GetProperty("item").GetProperty("id").GetGuid()).ToList();
        var library = await _server.ApiAsync("/api/library");
        var shelf = library.GetProperty("series").EnumerateArray().SelectMany(s => s.GetProperty("books").EnumerateArray())
            .Concat(library.GetProperty("standalone").EnumerateArray()).Select(b => b.GetProperty("id").GetGuid()).ToList();
        all.ShouldBe(shelf, ignoreOrder: true);
        all.ShouldContain(SeedIds.Books.Undertow);
    }

    [Test]
    [Order(3)]
    public async Task GetOutline_WhenOpeningUndertowByTitle_ShouldReturnItsPlan()
    {
        var body = await Client.OkAsync("get_outline", new { book = "Undertow" });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Books.Undertow);
        body.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.Undertow}/plan");
        (await _server.ApiAsync($"/api/books/{SeedIds.Books.Undertow}")).GetProperty("title").GetString().ShouldBe("Undertow");
    }

    [Test]
    [Order(4)]
    public async Task GetOutline_WhenTheTitleMatchesTwoBooks_ShouldBeAmbiguousAndListBoth()
    {
        var error = await Client.ErrorAsync("get_outline", new { book = "Product Engineering" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        error.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetGuid())
            .ShouldBe([SeedIds.Books.ProductEngineering, SeedIds.Books.ProductEngineeringV1], ignoreOrder: true);
    }

    [Test]
    [Order(5)]
    public async Task Search_WhenTheQueryMatchesNoBook_ShouldReturnNoHits()
    {
        var body = await Client.OkAsync("search", new { query = "zzzzqqqq", types = new[] { "book" } });

        body.GetProperty("hits").GetArrayLength().ShouldBe(0);
    }

    [Test]
    [Order(6)]
    public async Task Search_WhenTheQueryIsBlank_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("search", new { query = "  " });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }
}
