using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J2 · Open a book in a series, as the agent does it (#16 J2, #20 list_books, get_outline): find the series' books in
// order, open Hollow Throne by "<series>/<number>", and read the series-level Characters and Places from its Knowledge
// base index. The agent's calls are checked against what the Library and Plan screens read.
[TestFixture]
[Category("J2")]
[CancelAfter(120000)]
public class J2OpenSeriesBookTests
{
    private JourneyServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ListBooks_WhenFilteredToTheAshenCrown_ShouldReturnItsFourBooksInSeriesOrderWithTheLibraryNumbers()
    {
        var body = await Client.OkAsync("list_books", new { series = "The Ashen Crown", sort = "series_number" });

        var group = body.GetProperty("series").EnumerateArray().Single();
        var books = group.GetProperty("books").EnumerateArray().ToList();
        group.GetProperty("book_count").GetInt32().ShouldBe(4);
        books.Select(b => b.GetProperty("series_number").GetInt32()).ShouldBe([1, 2, 3, 4]);
        books[2].GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Books.HollowThrone);
        books[2].GetProperty("status").GetString().ShouldBe("revising");
        var library = await _server.ApiAsync("/api/library");
        var stack = library.GetProperty("series").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "The Ashen Crown");
        stack.GetProperty("bookCount").GetInt32().ShouldBe(4);
        stack.GetProperty("wordCount").GetInt32().ShouldBe(group.GetProperty("words").GetInt32());
        stack.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("id").GetGuid())
            .ShouldBe(books.Select(b => b.GetProperty("item").GetProperty("id").GetGuid()), ignoreOrder: true);
    }

    [Test]
    [Order(2)]
    public async Task GetOutline_WhenTheBookIsSeriesSlashNumber_ShouldOpenHollowThroneWithItsPlanUrl()
    {
        var body = await Client.OkAsync("get_outline", new { book = "The Ashen Crown/3", include = new[] { "kb_index" } });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Books.HollowThrone);
        body.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{SeedIds.Books.HollowThrone}/plan");
        body.GetProperty("kb_index").EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .ShouldBe(["Characters", "Places"], ignoreOrder: true);
    }

    [Test]
    [Order(3)]
    public async Task GetOutline_WhenTheKnowledgeBaseIsRead_ShouldMatchThePlanSidebarsSeriesCategories()
    {
        var kb = await _server.ApiAsync($"/api/books/{SeedIds.Books.HollowThrone}/kb");

        var categories = kb.GetProperty("categories").EnumerateArray().ToList();
        categories.Select(c => c.GetProperty("name").GetString()).ShouldBe(["Characters", "Places"], ignoreOrder: true);
        categories.ShouldAllBe(c => c.GetProperty("fromSeries").GetBoolean());
        kb.GetProperty("entries").GetArrayLength().ShouldBe(0);
    }

    [Test]
    [Order(4)]
    public async Task ListBooks_WhenTheSeriesIsUnknown_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("list_books", new { series = "The Ashen Throne" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(5)]
    public async Task GetOutline_WhenTheSeriesNumberDoesNotExist_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("get_outline", new { book = "The Ashen Crown/9" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }
}
