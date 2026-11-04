using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace Codex.Mcp.Tests;

// The read tools' options and the resources, over stdio against the seed: list_books filters, get_outline's within and
// include, every resource, links for series-owned entries, and read_manuscript paging that loses nothing at any page
// boundary. Nothing here writes, so the tests share one server.
[TestFixture]
[CancelAfter(60000)]
public class McpReadCoverageTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task Search_WhenAnEntryBelongsToASeries_ShouldOpenItInALiveBookOfTheSeriesNotTheSeries()
    {
        var body = await Client.OkAsync("search", new { query = "a", types = new[] { "kb_entry" }, limit = 100 });

        var hit = body.GetProperty("hits").EnumerateArray().Single(h => h.GetProperty("item").GetProperty("id").GetGuid() == SeedIds.KbEntries.EmberWard);
        hit.GetProperty("item").GetProperty("open_url").GetString()
            .ShouldBe($"/books/{await LatestBookOfAsync(SeedIds.Series.ForsakenGods)}/plan#entry={SeedIds.KbEntries.EmberWard}");
    }

    [Test]
    public async Task Search_WhenScopedToABookOfTheSeries_ShouldOpenTheEntryInThatBook()
    {
        var body = await Client.OkAsync("search",
            new { query = "Ember Ward", book = SeedIds.Books.BestiaryOfTheFallen.ToString(), types = new[] { "kb_entry" } });

        body.GetProperty("hits").EnumerateArray().Single().GetProperty("item").GetProperty("open_url").GetString()
            .ShouldBe($"/books/{SeedIds.Books.BestiaryOfTheFallen}/plan#entry={SeedIds.KbEntries.EmberWard}");
    }

    [Test]
    public async Task GetKbEntry_WhenASeriesEntryHasNoUses_ShouldOpenItInALiveBookOfTheSeries()
    {
        var body = await Client.OkAsync("get_kb_entry", new { entry = "Ember Ward" });

        body.GetProperty("scope").GetString().ShouldBe("series");
        body.GetProperty("item").GetProperty("open_url").GetString()
            .ShouldBe($"/books/{await LatestBookOfAsync(SeedIds.Series.ForsakenGods)}/plan#entry={SeedIds.KbEntries.EmberWard}");
    }

    [Test]
    public async Task FindKbUsages_WhenASeriesEntryHasNoUses_ShouldOpenItInALiveBookOfTheSeries()
    {
        var body = await Client.OkAsync("find_kb_usages", new { entry = SeedIds.KbEntries.EmberWard.ToString() });

        body.GetProperty("uses").GetArrayLength().ShouldBe(0);
        body.GetProperty("item").GetProperty("open_url").GetString()
            .ShouldBe($"/books/{await LatestBookOfAsync(SeedIds.Series.ForsakenGods)}/plan#kb={SeedIds.KbEntries.EmberWard}");
    }

    [Test]
    public async Task ListBooks_WhenFilteredBySeries_ShouldReturnOnlyThatSeriesAndNoStandaloneBooks()
    {
        var body = await Client.OkAsync("list_books", new { series = "The Ashen Crown" });

        var group = body.GetProperty("series").EnumerateArray().Single();
        group.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Series.AshenCrown);
        group.GetProperty("books").EnumerateArray().Select(b => b.GetProperty("item").GetProperty("id").GetGuid())
            .ShouldBe([SeedIds.Books.EmberOath, SeedIds.Books.IronLitany, SeedIds.Books.HollowThrone, SeedIds.Books.LastCinder], ignoreOrder: true);
        body.GetProperty("standalone").GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task ListBooks_WhenFilteredByStatus_ShouldReturnOnlyBooksWithThatStatusAndDropEmptySeries()
    {
        var body = await Client.OkAsync("list_books", new { status = new[] { "published" } });

        var books = body.GetProperty("series").EnumerateArray().SelectMany(g => g.GetProperty("books").EnumerateArray())
            .Concat(body.GetProperty("standalone").EnumerateArray()).ToList();
        books.ShouldAllBe(b => b.GetProperty("status").GetString() == "published");
        books.Select(b => b.GetProperty("item").GetProperty("id").GetGuid()).ShouldBe(await BooksWithStatusAsync("published"), ignoreOrder: true);
        body.GetProperty("series").EnumerateArray().Select(g => g.GetProperty("item").GetProperty("id").GetGuid())
            .ShouldNotContain(SeedIds.Series.ForsakenGods);
    }

    [TestCase("status", "archived")]
    [TestCase("sort", "newest")]
    public async Task ListBooks_WhenAFilterValueIsUnknown_ShouldFailInvalidNamingIt(string field, string value)
    {
        object arguments = field == "status" ? new { status = new[] { value } } : new { sort = value };

        var error = await Client.ErrorAsync("list_books", arguments);

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe(field);
    }

    [Test]
    public async Task ListBooks_WhenTheSeriesIsUnknown_ShouldFailNotFound()
    {
        var error = await Client.ErrorAsync("list_books", new { series = "No Such Series" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task GetOutline_WhenWithinIsAnAct_ShouldReturnOnlyThatAct()
    {
        var body = await Client.OkAsync("get_outline", new { book = ProductEngineering, within = "act:2" });

        var act = body.GetProperty("acts").EnumerateArray().Single();
        act.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(await SecondActAsync());
        body.GetProperty("item").GetProperty("number").GetString().ShouldBe("act:2");
    }

    [Test]
    public async Task GetOutline_WhenWithinIsAChapterByNumberOrSlug_ShouldReturnOnlyThatChapter()
    {
        var slug = await ChapterSlugAsync(SeedIds.Chapters.Ch5);

        foreach (var within in new[] { "5", $"ch:{slug}", SeedIds.Chapters.Ch5.ToString() })
        {
            var body = await Client.OkAsync("get_outline", new { book = ProductEngineering, within });

            var chapter = body.GetProperty("acts").EnumerateArray().Single().GetProperty("chapters").EnumerateArray().Single();
            chapter.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Chapters.Ch5);
            chapter.GetProperty("scenes").EnumerateArray().Select(s => s.GetProperty("item").GetProperty("id").GetGuid())
                .ShouldBe([SeedIds.Scenes.Ch5Sc1, SeedIds.Scenes.Ch5Sc2]);
        }
    }

    [Test]
    public async Task GetOutline_WhenWithinIsAScene_ShouldFailInvalid()
    {
        var error = await Client.ErrorAsync("get_outline", new { book = ProductEngineering, within = "5.2" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("within");
    }

    [Test]
    public async Task GetOutline_WhenIncludeNamesBeatsAndKbLinks_ShouldAddThemAndLeaveOutTheDefaults()
    {
        var body = await Client.OkAsync("get_outline", new { book = ProductEngineering, within = "5", include = new[] { "beats", "kb_links" } });

        var scene = Scenes(body).Single(s => s.GetProperty("item").GetProperty("id").GetGuid() == SeedIds.Scenes.Ch5Sc2);
        scene.GetProperty("beats").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ShouldContain(SeedIds.Beats.Ch5Sc2OutcomeMetrics);
        scene.TryGetProperty("kb_links", out var links).ShouldBeTrue();
        links.ValueKind.ShouldBe(JsonValueKind.Array);
        scene.TryGetProperty("summary", out _).ShouldBeFalse();
        scene.TryGetProperty("labels", out _).ShouldBeFalse();
        scene.TryGetProperty("words", out _).ShouldBeFalse();
    }

    [Test]
    public async Task GetOutline_WhenIncludeIsDefault_ShouldHaveSummariesLabelsAndWordsButNoBeats()
    {
        var body = await Client.OkAsync("get_outline", new { book = ProductEngineering, within = "5" });

        var scene = Scenes(body).Single(s => s.GetProperty("item").GetProperty("id").GetGuid() == SeedIds.Scenes.Ch5Sc2);
        scene.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ShouldBe(["Needs research"]);
        scene.TryGetProperty("words", out _).ShouldBeTrue();
        scene.TryGetProperty("beats", out _).ShouldBeFalse();
        scene.TryGetProperty("kb_links", out _).ShouldBeFalse();
    }

    [Test]
    public async Task GetOutline_WhenIncludeNamesArchived_ShouldListTheArchivedChapters()
    {
        var body = await Client.OkAsync("get_outline", new { book = SeedIds.Books.HymnsForMachines.ToString(), include = new[] { "archived" } });

        body.GetProperty("archived_chapters").EnumerateArray().Select(c => c.GetProperty("item").GetProperty("id").GetGuid())
            .ShouldBe([SeedIds.Chapters.HymnsArchivedCh]);
    }

    [Test]
    public async Task ReadResource_WhenTheLibraryIsRead_ShouldBeWhatListBooksReturns()
    {
        var resource = await ResourceAsync("codex://books");

        resource.ToString().ShouldBe((await Client.OkAsync("list_books")).ToString());
    }

    [Test]
    public async Task ReadResource_WhenAnOutlineIsRead_ShouldBeWhatGetOutlineReturns()
    {
        var resource = await ResourceAsync($"codex://book/{ProductEngineering}/outline");

        resource.ToString().ShouldBe((await Client.OkAsync("get_outline", new { book = ProductEngineering })).ToString());
    }

    [Test]
    public async Task ReadResource_WhenTheStyleGuideIsRead_ShouldBeTheMergedGuideGetBookSettingsReturns()
    {
        var result = await Client.ReadResourceAsync($"codex://book/{ProductEngineering}/style-guide", cancellationToken: McpCalls.Token);

        var settings = await Client.OkAsync("get_book_settings", new { book = ProductEngineering });
        result.Contents.OfType<TextResourceContents>().Single().Text
            .ShouldBe(settings.GetProperty("style_guide").GetProperty("merged").GetString());
    }

    [Test]
    public async Task ReadResource_WhenAnEntryIsRead_ShouldBeWhatGetKbEntryReturns()
    {
        var resource = await ResourceAsync($"codex://kb/{SeedIds.KbEntries.GoodhartsLaw}");

        resource.ToString().ShouldBe((await Client.OkAsync("get_kb_entry", new { entry = SeedIds.KbEntries.GoodhartsLaw.ToString() })).ToString());
    }

    [Test]
    public async Task ReadResource_WhenReferencesAreRead_ShouldHaveTheSummarySourcesAndUnflaggedQuotes()
    {
        var resource = await ResourceAsync($"codex://book/{ProductEngineering}/references");

        resource.GetProperty("item").GetProperty("open_url").GetString().ShouldBe($"/books/{ProductEngineering}/references");
        resource.GetProperty("summary").ValueKind.ShouldBe(JsonValueKind.Object);
        resource.GetProperty("sources").ValueKind.ShouldNotBe(JsonValueKind.Null);
        resource.GetProperty("unflagged_quotes").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task ReadResource_WhenTheBookDoesNotExist_ShouldFailWithTheCode()
    {
        var error = await Should.ThrowAsync<ModelContextProtocol.McpException>(
            () => Client.ReadResourceAsync($"codex://book/{Guid.NewGuid()}/outline", cancellationToken: McpCalls.Token).AsTask());

        error.Message.ShouldContain("not_found");
    }

    [TestCase("markdown", 1)]
    [TestCase("markdown", 40)]
    [TestCase("markdown", 350)]
    [TestCase("annotated", 1)]
    [TestCase("annotated", 40)]
    [TestCase("annotated", 350)]
    public async Task ReadManuscript_WhenPaged_ShouldJoinToExactlyTheSingleRead(string format, int maxWords)
    {
        var single = (await Client.OkAsync("read_manuscript", new { book = ProductEngineering, scope = "5", format, max_words = 20000 }))
            .GetProperty("scenes").EnumerateArray().ToList();
        var parts = new List<(Guid Scene, string Text)>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await Client.OkAsync("read_manuscript", new { book = ProductEngineering, scope = "5", format, max_words = maxWords, cursor });
            pages++;
            foreach (var scene in page.GetProperty("scenes").EnumerateArray())
            {
                var text = scene.GetProperty(format == "markdown" ? "body" : "text").GetString()!;
                text.ShouldNotBeEmpty();
                parts.Add((scene.GetProperty("item").GetProperty("id").GetGuid(), text));
            }
            cursor = page.TryGetProperty("cursor", out var next) ? next.GetString() : null;
        }
        while (cursor != null && pages < 500);

        cursor.ShouldBeNull();
        var joined = parts.GroupBy(p => p.Scene)
            .Select(g => (Scene: g.Key, Text: format == "markdown" ? string.Concat(g.Select(p => p.Text)) : string.Join("\n\n", g.Select(p => p.Text))))
            .ToList();
        joined.Select(j => j.Scene).ShouldBe(single.Select(s => s.GetProperty("item").GetProperty("id").GetGuid()));
        joined.Select(j => j.Text).ShouldBe(single.Select(s => s.GetProperty(format == "markdown" ? "body" : "text").GetString()));
        if (format == "markdown")
        {
            joined.Select(j => j.Text).ShouldBe([await ProseBodyAsync(SeedIds.Scenes.Ch5Sc1), await ProseBodyAsync(SeedIds.Scenes.Ch5Sc2)]);
        }
    }

    private static IEnumerable<JsonElement> Scenes(JsonElement outline) =>
        outline.GetProperty("acts").EnumerateArray().SelectMany(a => a.GetProperty("chapters").EnumerateArray())
            .SelectMany(c => c.GetProperty("scenes").EnumerateArray());

    private async Task<JsonElement> ResourceAsync(string uri)
    {
        var result = await Client.ReadResourceAsync(uri, cancellationToken: McpCalls.Token);
        return JsonDocument.Parse(result.Contents.OfType<TextResourceContents>().Single().Text).RootElement.Clone();
    }

    private async Task<Guid> LatestBookOfAsync(Guid seriesId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM book WHERE series_id = @seriesId AND archived_at IS NULL ORDER BY last_edited_at DESC, id LIMIT 1", new { seriesId });
    }

    private async Task<List<Guid>> BooksWithStatusAsync(string status)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return (await connection.QueryAsync<Guid>(
            "SELECT id FROM book WHERE kind = 'book' AND status = @status AND archived_at IS NULL", new { status })).ToList();
    }

    private async Task<Guid> SecondActAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM act WHERE book_id = @bookId AND archived_at IS NULL ORDER BY position, id OFFSET 1 LIMIT 1",
            new { bookId = SeedIds.Books.ProductEngineering });
    }

    private async Task<string> ChapterSlugAsync(Guid chapterId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT slug FROM chapter WHERE id = @chapterId", new { chapterId }) ?? "";
    }

    private async Task<string> ProseBodyAsync(Guid sceneId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @sceneId", new { sceneId }) ?? "";
    }
}
