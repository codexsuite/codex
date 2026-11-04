using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Journeys;

// J13 · Focused writing session, read side (#16 J13; #20 leaves Focus, Scope and Format out as view settings): what the agent
// can do is read the scope the author is focused on, Chapter 6, and get the same words the Plan screen counts. Reading never
// writes: no change set, no new revision.
[TestFixture]
[Category("J13")]
[CancelAfter(120000)]
public class J13FocusReadSideTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private JourneyServer _server = null!;
    private int _version;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await JourneyServer.StartAsync();
        _version = (await _server.ProseAsync(SeedIds.Scenes.Ch6Sc1)).Version;
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ReadManuscript_WhenScopedToChapter6_ShouldReturnOnlyItsScenesWithTheSameWordsAsThePlanScreen()
    {
        var body = await Client.OkAsync("read_manuscript", new { book = Book, scope = "6" });

        var scenes = body.GetProperty("scenes").EnumerateArray().ToList();
        scenes.Select(s => s.GetProperty("item").GetProperty("number").GetString()).ShouldBe(["6.1", "6.2"]);
        var outline = await _server.ApiAsync($"/api/books/{Book}/outline");
        var chapter = outline.GetProperty("acts").EnumerateArray().SelectMany(a => a.GetProperty("chapters").EnumerateArray())
            .Single(c => c.GetProperty("number").GetInt32() == 6);
        body.GetProperty("words").GetInt32().ShouldBe(chapter.GetProperty("scenes").EnumerateArray().Sum(s => s.GetProperty("words").GetInt32()));
        scenes.ShouldAllBe(s => s.GetProperty("version").GetInt32() > 0);
    }

    [Test]
    [Order(2)]
    public async Task ReadManuscript_WhenWidenedToTheFullManuscript_ShouldCountTheWholeBook()
    {
        var chapter = await Client.OkAsync("read_manuscript", new { book = Book, scope = "6", format = "markdown" });
        var full = await Client.OkAsync("read_manuscript", new { book = Book, max_words = 20000, format = "markdown" });

        full.GetProperty("words").GetInt32().ShouldBeGreaterThan(chapter.GetProperty("words").GetInt32());
        full.GetProperty("scenes").GetArrayLength().ShouldBeGreaterThan(2);
    }

    [Test]
    [Order(3)]
    public async Task ReadManuscript_WhenTheScopeIsNotInTheBook_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("read_manuscript", new { book = Book, scope = "42" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    [Order(4)]
    public async Task ReadManuscript_WhenTheFormatIsUnknown_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("read_manuscript", new { book = Book, scope = "6", format = "epub" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(5)]
    public async Task ReadManuscript_WhenRead_ShouldWriteNothing()
    {
        (await _server.ScalarAsync<int>("SELECT count(*)::int FROM change_set")).ShouldBe(0);
        (await _server.ProseAsync(SeedIds.Scenes.Ch6Sc1)).Version.ShouldBe(_version);
    }
}
