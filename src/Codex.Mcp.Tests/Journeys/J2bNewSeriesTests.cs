using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J2 variant · Make a new series and put a standalone book in it (#73), as the agent does it: create_series with the book,
// refused until confirmed, then list_books shows the book under the series as number 1. Then the tidy-up with
// update_series (rename, describe, reorder, archive), each undone. One scenario on its own clone, in order.
[TestFixture]
[Category("J2")]
[CancelAfter(120000)]
public class J2bNewSeriesTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string HollowThrone = SeedIds.Books.HollowThrone.ToString();
    private JourneyServer _server = null!;
    private Guid _seriesId;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task CreateSeries_WhenTheNameIsBlank_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("create_series", new { name = "  " });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("name");
    }

    [Test]
    [Order(2)]
    public async Task CreateSeries_WhenBooksAreGivenWithoutConfirming_ShouldNeedConfirmationAndWriteNothing()
    {
        var error = await Client.ErrorAsync("create_series", new { name = "Lantern Wars", books = new[] { Book } });

        error.GetProperty("code").GetString().ShouldBe("needs_confirmation");
        (await Client.OkAsync("list_books", new { series = "The Ashen Crown" })).GetProperty("series").GetArrayLength().ShouldBe(1);
        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldNotContain(s => s.GetProperty("name").GetString() == "Lantern Wars");
    }

    [Test]
    [Order(3)]
    public async Task CreateSeries_WhenDryRun_ShouldReportTheJoinAndWriteNothing()
    {
        var result = await Client.OkAsync("create_series", new { name = "Lantern Wars", books = new[] { Book }, dry_run = true });

        result.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        result.GetProperty("side_effects").EnumerateArray().Select(s => s.GetString()).ShouldContain(s => s!.Contains("Join Lantern Wars as book 1"));
        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldNotContain(s => s.GetProperty("name").GetString() == "Lantern Wars");
    }

    [Test]
    [Order(4)]
    public async Task CreateSeries_WhenConfirmed_ShouldPutTheBookInAsNumberOneAndListBooksShowsIt()
    {
        var result = await Client.OkAsync("create_series", new
        {
            name = "Lantern Wars", description = "A war of small gods.", author_name = "J. Dickson", books = new[] { Book }, confirm = true,
        });

        _seriesId = result.GetProperty("item").GetProperty("id").GetGuid();
        result.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/#series={_seriesId}");
        result.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
        var listed = await Client.OkAsync("list_books", new { series = "Lantern Wars" });
        var group = listed.GetProperty("series").EnumerateArray().Single();
        group.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(_seriesId);
        var book = group.GetProperty("books").EnumerateArray().Single();
        book.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Books.ProductEngineering);
        book.GetProperty("series_number").GetInt32().ShouldBe(1);
        var settings = await _server.ApiAsync($"/api/books/{Book}/settings");
        settings.GetProperty("seriesId").GetGuid().ShouldBe(_seriesId);
        settings.GetProperty("seriesNumber").GetInt32().ShouldBe(1);
    }

    [Test]
    [Order(5)]
    public async Task Undo_WhenTheSeriesCreationMovedABook_ShouldBeBlocked()
    {
        var result = await Client.OkAsync("create_series", new { name = "Second Gods", books = new[] { HollowThrone }, confirm = true });

        var error = await Client.ErrorAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("blocked");
        error.GetProperty("rule").GetString().ShouldBe("undo_unsupported");
        (await _server.ApiAsync($"/api/books/{HollowThrone}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(1);
    }

    [Test]
    [Order(6)]
    public async Task CreateSeries_WhenEmpty_ShouldBeUndoneByArchivingIt()
    {
        var result = await Client.OkAsync("create_series", new { name = "Scratch" });
        result.GetProperty("books").GetArrayLength().ShouldBe(0);

        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldNotContain(s => s.GetProperty("name").GetString() == "Scratch");
    }

    [Test]
    [Order(7)]
    public async Task UpdateSeries_WhenRenamedAndDescribed_ShouldChangeItAndUndoShouldPutItBack()
    {
        var result = await Client.OkAsync("update_series", new { series_ref = "Lantern Wars", name = "Lantern Wars I", description = "" });

        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldContain(s => s.GetProperty("id").GetGuid() == _seriesId && s.GetProperty("name").GetString() == "Lantern Wars I");

        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldContain(s => s.GetProperty("id").GetGuid() == _seriesId && s.GetProperty("name").GetString() == "Lantern Wars");
    }

    [Test]
    [Order(8)]
    public async Task UpdateSeries_WhenReordered_ShouldRenumberAndUndoShouldPutTheOrderBack()
    {
        // Hollow Throne is in the series ahead of nothing else: add the first book as 2, then swap.
        await Client.OkAsync("update_book", new { book = HollowThrone, series = "Lantern Wars", confirm = true });
        var result = await Client.OkAsync("update_series", new { series_ref = "Lantern Wars", order = new[] { HollowThrone, Book } });

        (await _server.ApiAsync($"/api/books/{HollowThrone}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(1);
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(2);
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(1);
    }

    [Test]
    [Order(9)]
    public async Task UpdateSeries_WhenOrderMissesABook_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("update_series", new { series_ref = "Lantern Wars", order = new[] { Book } });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("order");
    }

    [Test]
    [Order(10)]
    public async Task UpdateSeries_WhenArchivingASeriesWithBooks_ShouldHideItsBooksAndUndoShouldBringThemBackNumbered()
    {
        var result = await Client.OkAsync("update_series", new { series_ref = "Lantern Wars", archive = true });

        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldNotContain(s => s.GetProperty("name").GetString() == "Lantern Wars");
        var listed = await Client.OkAsync("list_books");
        listed.GetRawText().ShouldNotContain("Lantern Wars");
        listed.GetProperty("standalone").EnumerateArray().ShouldNotContain(b => b.GetProperty("item").GetProperty("id").GetString() == Book);
        (await Client.OkAsync("search", new { query = "Product Engineering", types = new[] { "book" } }))
            .GetProperty("hits").EnumerateArray().ShouldNotContain(h => h.GetProperty("item").GetProperty("id").GetString() == Book);
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(1);

        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldContain(s => s.GetProperty("name").GetString() == "Lantern Wars");
        (await _server.ApiAsync($"/api/books/{Book}/settings")).GetProperty("seriesNumber").GetInt32().ShouldBe(1);
    }

    [Test]
    [Order(11)]
    public async Task UpdateSeries_WhenArchivingAnEmptySeries_ShouldArchiveItAndUndoShouldRestoreIt()
    {
        var made = await Client.OkAsync("create_series", new { name = "Empty Shelf" });
        var result = await Client.OkAsync("update_series", new { series_ref = "Empty Shelf", archive = true });
        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldNotContain(s => s.GetProperty("name").GetString() == "Empty Shelf");

        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        (await _server.ApiAsync("/api/library")).GetProperty("series").EnumerateArray()
            .ShouldContain(s => s.GetProperty("name").GetString() == "Empty Shelf");
        made.GetProperty("undo_id").GetString().ShouldNotBeNullOrEmpty();
    }
}
