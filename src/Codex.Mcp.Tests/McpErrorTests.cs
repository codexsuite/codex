using System.Text.Json;
using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using Npgsql;

namespace Codex.Mcp.Tests;

// How refs and arguments fail (#20 2.1, 2.3, M11): ambiguous and out-of-scope refs, numbers too big for any outline,
// and the SDK's own argument binding failures, all as error results with a code (never internal_error). Its own
// server and clone, because one test adds a Knowledge base entry.
[TestFixture]
[CancelAfter(60000)]
public class McpErrorTests
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private readonly Guid _secondGoodhart = Guid.NewGuid();

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        // A series character also called "Goodhart", so the name names two entries across the Library.
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO kb_entry (id, book_id, category_id, name, position, aliases, summary, body)
            VALUES (@id, @series, @category, 'Goodhart', 'z0', '{}', '', '')
            """,
            new { id = _secondGoodhart, series = SeedIds.Series.AshenCrown, category = SeedIds.KbCategories.AshenCrownCharacters });
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [TestCase("99999999999")]
    [TestCase("5.99999999999")]
    [TestCase("99999999999.1")]
    [TestCase("act:99999999999")]
    public async Task ReadManuscript_WhenTheNumberIsTooBigForAnyOutline_ShouldFailNotFound(string scope)
    {
        var error = await _server.Client.ErrorAsync("read_manuscript", new { book = ProductEngineering, scope });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task GetOutline_WhenTheSeriesNumberIsTooBigForAnyBook_ShouldFailNotFound()
    {
        var error = await _server.Client.ErrorAsync("get_outline", new { book = "The Ashen Crown/99999999999" });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task ReadManuscript_WhenChapterSlugIsEmpty_ShouldFailInvalid()
    {
        var error = await _server.Client.ErrorAsync("read_manuscript", new { book = ProductEngineering, scope = "ch:" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("ref");
    }

    [Test]
    public async Task ReadManuscript_WhenTheScopesBookIsAmbiguous_ShouldFailAmbiguousWithBothBooks()
    {
        // An ItemRef is resolved inside one book; numbers and slugs are unique per book (chapter_slug_uq), so the
        // ambiguity an item ref can meet is its book's.
        var error = await _server.Client.ErrorAsync("read_manuscript", new { book = "Product Engineering", scope = "5.2" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        Ids(error.GetProperty("candidates")).ShouldBe([SeedIds.Books.ProductEngineering, SeedIds.Books.ProductEngineeringV1], ignoreOrder: true);
    }

    [Test]
    public async Task GetKbEntry_WhenTheNameNamesTwoEntriesInTheLibrary_ShouldFailAmbiguousWithBoth()
    {
        var error = await _server.Client.ErrorAsync("get_kb_entry", new { entry = "Goodhart" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
        Ids(error.GetProperty("candidates")).ShouldBe([SeedIds.KbEntries.GoodhartsLaw, _secondGoodhart], ignoreOrder: true);
    }

    [Test]
    public async Task GetKbEntry_WhenTheNameIsAmbiguousButOnlyOneIsInTheBooksScope_ShouldReturnThatOne()
    {
        var body = await _server.Client.OkAsync("get_kb_entry", new { entry = "Goodhart", book = ProductEngineering });

        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.KbEntries.GoodhartsLaw);
    }

    [Test]
    public async Task GetKbEntry_WhenTheEntryIsOutsideTheBooksScope_ShouldFailOutOfScopeWithTheScopeChecked()
    {
        var error = await _server.Client.ErrorAsync("get_kb_entry", new { entry = "Ember Ward", book = "The Ashen Crown/3" });

        error.GetProperty("code").GetString().ShouldBe("out_of_scope");
        Ids(error.GetProperty("scope")).ShouldBe([SeedIds.Books.HollowThrone, SeedIds.Series.AshenCrown]);
        Ids(error.GetProperty("candidates")).ShouldBe([SeedIds.KbEntries.EmberWard]);
    }

    [Test]
    public async Task GetKbEntry_WhenNoEntryHasTheName_ShouldStillFailNotFound()
    {
        var error = await _server.Client.ErrorAsync("get_kb_entry", new { entry = "No such entry", book = ProductEngineering });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task Search_WhenAnArgumentHasTheWrongType_ShouldFailInvalidNamingIt()
    {
        var error = await _server.Client.ErrorAsync("search", new { query = "a", limit = "many" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("limit");
    }

    [Test]
    public async Task GetDraft_WhenTheIdIsNotAnId_ShouldFailInvalidNamingIt()
    {
        var error = await _server.Client.ErrorAsync("get_draft", new { draft = "not-an-id" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("draft");
    }

    [Test]
    public async Task Search_WhenTheRequiredArgumentIsMissing_ShouldFailInvalidNamingIt()
    {
        var error = await _server.Client.ErrorAsync("search", new { limit = 3 });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("query");
    }

    [Test]
    public async Task CallTool_WhenTheToolDoesNotExist_ShouldFailInvalid()
    {
        var error = await _server.Client.ErrorAsync("no_such_tool");

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("name");
    }

    [TestCase(0)]
    [TestCase(20001)]
    public async Task ReadManuscript_WhenMaxWordsIsOutsideOneToTwentyThousand_ShouldFailInvalid(int maxWords)
    {
        var error = await _server.Client.ErrorAsync("read_manuscript", new { book = ProductEngineering, scope = "5.2", max_words = maxWords });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("max_words");
    }

    [Test]
    public async Task ReadManuscript_WhenMaxWordsIsTheCap_ShouldRead()
    {
        var body = await _server.Client.OkAsync("read_manuscript", new { book = ProductEngineering, scope = "5.2", max_words = 20000 });

        body.GetProperty("scenes").GetArrayLength().ShouldBe(1);
    }

    private static List<Guid> Ids(JsonElement items) => items.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
}
