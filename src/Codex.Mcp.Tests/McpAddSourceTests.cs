using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// add_source over stdio on the Product Engineering seed (#20 review 2, J9B's "add it for later"). One scenario on its own clone,
// in order: a dry run that writes nothing, a typed-in source added as Not cited (then archived by undo), an ISBN the book
// already has answering that source and writing nothing, and a source that is not an object.
[TestFixture]
[CancelAfter(120000)]
public class McpAddSourceTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly object Typed = new { @new = new { kind = "book", title = "The Lean Startup", authors = "Ries, Eric", year = 2011 } };
    private McpStdioServer _server = null!;

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, args) ?? throw new InvalidOperationException("no value");
    }

    private Task<int> LiveSourcesAsync() =>
        ScalarAsync<int>("SELECT count(*)::int FROM reference WHERE book_id = @book AND archived_at IS NULL", new { book = SeedIds.Books.ProductEngineering });

    [Test]
    [Order(1)]
    public async Task AddSource_WhenDryRun_ShouldReportWithoutWriting()
    {
        var before = await LiveSourcesAsync();

        var body = await Client.OkAsync("add_source", new { book = Book, source = Typed, dry_run = true });

        body.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        body.GetProperty("how").GetString().ShouldBe("added");
        body.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await LiveSourcesAsync()).ShouldBe(before);
    }

    [Test]
    [Order(2)]
    public async Task AddSource_WhenTypedIn_ShouldAddItNotCitedAndUndoShouldArchiveIt()
    {
        var before = await LiveSourcesAsync();

        var body = await Client.OkAsync("add_source", new { book = Book, source = Typed });

        body.GetProperty("how").GetString().ShouldBe("added");
        body.GetProperty("status").GetString().ShouldBe("not_cited");
        var id = body.GetProperty("item").GetProperty("id").GetGuid();
        body.GetProperty("item").GetProperty("label").GetString().ShouldBe("The Lean Startup");
        (await LiveSourcesAsync()).ShouldBe(before + 1);
        (await ScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE reference_id = @id", new { id })).ShouldBe(0);
        (await ScalarAsync<string>("SELECT source FROM reference_event WHERE reference_id = @id AND action = 'added'", new { id })).ShouldBe("agent");

        await Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await ScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference WHERE id = @id", new { id })).ShouldBeTrue();
        (await LiveSourcesAsync()).ShouldBe(before);
    }

    [Test]
    [Order(3)]
    public async Task AddSource_WhenTheBookHasTheIsbn_ShouldAnswerThatSourceAndWriteNothing()
    {
        // The seed's sources carry no ISBN, so give Lean Software Development (Not cited) one first.
        await Client.OkAsync("update_source", new { source = "Lean Software Development", book = Book, fields = new { isbn = "9780321150783" } });
        var before = await LiveSourcesAsync();

        var body = await Client.OkAsync("add_source", new { book = Book, source = new { isbn = "978-0-321-15078-3" } });

        body.GetProperty("how").GetString().ShouldBe("matched");
        body.GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.References.LeanSoftware);
        body.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await LiveSourcesAsync()).ShouldBe(before);
    }

    [Test]
    [Order(4)]
    public async Task AddSource_WhenSourceIsARef_ShouldFailInvalidPointingAtCiteSource()
    {
        var error = await Client.ErrorAsync("add_source", new { book = Book, source = "#4" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("source");
        error.GetProperty("message").GetString()!.ShouldContain("cite_source");
    }
}
