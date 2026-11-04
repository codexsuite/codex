using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests.Journeys;

// J3 · Start a new book, as the agent does it (#16 J3, #20 create_book): A is a standalone book, B is Book 005 of The
// Ashen Crown. The result envelope, the Plan URL, the HTTP API seeing the book, undo, and the errors.
[TestFixture]
[Category("J3")]
[CancelAfter(120000)]
public class J3StartNewBookTests
{
    private JourneyServer _server = null!;
    private McpClient Client => _server.Client;

    private string ConnectionString => _server.ConnectionString;

    private string _standalone = "";
    private string _standaloneUndo = "";
    private string _inSeries = "";

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await JourneyServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private Task<JsonElement> ApiAsync(string path) => _server.ApiAsync(path);

    [Test]
    [Order(1)]
    public async Task CreateBook_WhenTheTitleIsEmpty_ShouldBeInvalidAndWriteNothing()
    {
        var before = await BookCountAsync();

        var error = await Client.ErrorAsync("create_book", new { title = "   " });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        (await BookCountAsync()).ShouldBe(before);
        (await TitleCountAsync("   ")).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task CreateBook_WhenTheSeriesIsUnknown_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("create_book", new { title = "Orphan", series = "No Such Series" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await TitleCountAsync("Orphan")).ShouldBe(0);
    }

    [Test]
    [Order(3)]
    public async Task CreateBook_WhenStandalone_ShouldCreateItWithTheTargetAndOneAct()
    {
        var result = await Client.OkAsync("create_book", new { title = "Field Notes", target_word_count = 60000 });

        _standalone = result.GetProperty("item").GetProperty("id").GetString()!;
        _standaloneUndo = result.GetProperty("undo_id").GetString()!;
        result.GetProperty("item").GetProperty("kind").GetString().ShouldBe("book");
        result.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{_standalone}/plan");
        result.GetProperty("title").GetString().ShouldBe("Field Notes");
        result.GetProperty("side_effects").GetArrayLength().ShouldBeGreaterThan(0);
        var affected = result.GetProperty("affected").EnumerateArray().Single();
        affected.GetProperty("kind").GetString().ShouldBe("book");
        affected.GetProperty("id").GetString().ShouldBe(_standalone);
        affected.GetProperty("field").GetString().ShouldBe("created");
        result.TryGetProperty("version", out _).ShouldBeFalse();
    }

    [Test]
    [Order(4)]
    public async Task CreateBook_WhenCreated_ShouldBeVisibleThroughTheHttpApi()
    {
        var settings = await ApiAsync($"/api/books/{_standalone}/settings");
        settings.GetProperty("title").GetString().ShouldBe("Field Notes");
        settings.GetProperty("targetWords").GetInt32().ShouldBe(60000);
        settings.GetProperty("seriesId").ValueKind.ShouldBe(JsonValueKind.Null);

        var outline = await ApiAsync($"/api/books/{_standalone}/outline");
        outline.GetProperty("acts").GetArrayLength().ShouldBe(1);

        var library = await ApiAsync("/api/library");
        library.GetProperty("standalone").EnumerateArray().Select(b => b.GetProperty("id").GetString()).ShouldContain(_standalone);
    }

    [Test]
    [Order(5)]
    public async Task CreateBook_WhenAddingChapterOneThenAScene_ShouldLayOutTheNewBook()
    {
        // J3 step 4: "Chapter 1, then a scene". Refs only name what was there before the batch, so the scene goes in a
        // second batch, into the chapter's number.
        var act = (await Client.OkAsync("get_outline", new { book = _standalone })).GetProperty("acts")[0].GetProperty("item").GetProperty("id").GetString();

        await Client.OkAsync("edit_outline", new
        {
            book = _standalone, ops = new object[] { new { op = "add", kind = "chapter", title = "Chapter 1", into = act } },
        });
        await Client.OkAsync("get_outline", new { book = _standalone });
        await Client.OkAsync("edit_outline", new
        {
            book = _standalone, ops = new object[] { new { op = "add", kind = "scene", title = "Arrival", into = "1" } },
        });

        var chapter = (await ApiAsync($"/api/books/{_standalone}/outline")).GetProperty("acts")[0].GetProperty("chapters").EnumerateArray().ShouldHaveSingleItem();
        chapter.GetProperty("title").GetString().ShouldBe("Chapter 1");
        chapter.GetProperty("number").GetInt32().ShouldBe(1);
        chapter.GetProperty("scenes").EnumerateArray().ShouldHaveSingleItem().GetProperty("title").GetString().ShouldBe("Arrival");
    }

    [Test]
    [Order(6)]
    public async Task CreateBook_WhenIntoASeries_ShouldTakeTheNextNumberAndShareTheSeriesKnowledgeBase()
    {
        var result = await Client.OkAsync("create_book", new { title = "Ashfall", series = "The Ashen Crown" });

        _inSeries = result.GetProperty("item").GetProperty("id").GetString()!;
        result.GetProperty("series_number").GetInt32().ShouldBe(5);
        result.GetProperty("series").GetProperty("id").GetGuid().ShouldBe(SeedIds.Series.AshenCrown);
        result.GetProperty("side_effects").EnumerateArray().Select(s => s.GetString()).ShouldContain(s => s!.Contains("Characters and Places"));
        var settings = await ApiAsync($"/api/books/{_inSeries}/settings");
        settings.GetProperty("seriesNumber").GetInt32().ShouldBe(5);
        settings.GetProperty("seriesId").GetGuid().ShouldBe(SeedIds.Series.AshenCrown);
        (await Client.OkAsync("get_outline", new { book = _inSeries, include = new[] { "kb_index" } }))
            .GetProperty("kb_index").EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .ShouldBe(["Characters", "Places"], ignoreOrder: true);
    }

    [Test]
    [Order(7)]
    public async Task CreateBook_WhenTheSeriesNumberIsTaken_ShouldConflictAndWriteNothing()
    {
        var error = await Client.ErrorAsync("create_book", new { title = "Clash", series = "The Ashen Crown", series_number = 3 });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await TitleCountAsync("Clash")).ShouldBe(0);
    }

    [Test]
    [Order(8)]
    public async Task Undo_WhenTheBookWasCreated_ShouldArchiveIt()
    {
        await Client.OkAsync("undo", new { undo_id = _standaloneUndo });

        var library = await ApiAsync("/api/library");
        library.GetProperty("standalone").EnumerateArray().Select(b => b.GetProperty("id").GetString()).ShouldNotContain(_standalone);
        await using var connection = new NpgsqlConnection(ConnectionString);
        (await connection.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM book WHERE id = @id", new { id = Guid.Parse(_standalone) })).ShouldBeTrue();
    }

    private async Task<int> BookCountAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book");
    }

    private async Task<int> TitleCountAsync(string title)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book WHERE title = @title", new { title });
    }
}
