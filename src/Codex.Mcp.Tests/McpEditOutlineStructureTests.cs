using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// edit_outline on chapters and acts: add (a dry run first, then for real, then undo), archive, restore through the
// batch's own restore op, and undo of each. Also add before the first sibling, which the create commands would append.
// One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(60000)]
public class McpEditOutlineStructureTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Ch3 = SeedIds.Chapters.Ch3.ToString();
    private static readonly string Ch7 = SeedIds.Chapters.Ch7.ToString();
    private static readonly string Practices = SeedIds.Acts.Practices.ToString();
    private McpStdioServer _server = null!;
    private string _addUndo = "";
    private string _archiveUndo = "";
    private string _restoreUndo = "";

    private McpClient Client => _server.Client;

    private static readonly object[] AddOps =
    [
        new { op = "add", kind = "chapter", title = "Measuring Flow", after = Ch7 },
        new { op = "add", kind = "act", title = "Appendices", after = Practices },
        new { op = "add", kind = "scene", title = "Cold open", before = "4.1" },
    ];

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task EditOutline_WhenAddingAChapterAndAnActAsADryRun_ShouldReportRenumberingAndWriteNothing()
    {
        var body = await Client.OkAsync("edit_outline", new { book = Book, ops = AddOps, dry_run = true });

        Renumbered(body).ShouldBe(
            [(SeedIds.Chapters.Ch8, "Ch 8", "Ch 9"), (SeedIds.Chapters.Ch9, "Ch 9", "Ch 10"), (SeedIds.Chapters.Ch10, "Ch 10", "Ch 11")],
            ignoreOrder: true);
        (await LiveCountAsync("SELECT count(*)::int FROM chapter WHERE title = 'Measuring Flow' AND archived_at IS NULL")).ShouldBe(0);
        (await LiveCountAsync("SELECT count(*)::int FROM act WHERE title = 'Appendices' AND archived_at IS NULL")).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task EditOutline_WhenAddingAChapterAnActAndAFirstScene_ShouldPlaceEachWhereTheBatchSaid()
    {
        var body = await Client.OkAsync("edit_outline", new { book = Book, ops = AddOps });

        _addUndo = body.GetProperty("undo_id").GetString()!;
        (await ChapterNumberAsync("Measuring Flow")).ShouldBe(8);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch8)).ShouldBe(9);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch10)).ShouldBe(11);
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.QueryAsync<string>(
            "SELECT title FROM act WHERE book_id = @book AND archived_at IS NULL ORDER BY position", new { book = SeedIds.Books.ProductEngineering }))
            .Last().ShouldBe("Appendices");
        (await connection.ExecuteScalarAsync<string?>(
            "SELECT title FROM scene WHERE chapter_id = @chapter AND archived_at IS NULL ORDER BY position LIMIT 1", new { chapter = SeedIds.Chapters.Ch4 }))
            .ShouldBe("Cold open");
    }

    [Test]
    [Order(3)]
    public async Task Undo_WhenTheBatchAddedAChapterAndAnAct_ShouldArchiveThemAndRenumberBack()
    {
        await Client.OkAsync("undo", new { undo_id = _addUndo });

        (await LiveCountAsync("SELECT count(*)::int FROM chapter WHERE title = 'Measuring Flow' AND archived_at IS NULL")).ShouldBe(0);
        (await LiveCountAsync("SELECT count(*)::int FROM act WHERE title = 'Appendices' AND archived_at IS NULL")).ShouldBe(0);
        (await LiveCountAsync("SELECT count(*)::int FROM scene WHERE title = 'Cold open' AND archived_at IS NULL")).ShouldBe(0);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch8)).ShouldBe(8);
    }

    [Test]
    [Order(4)]
    public async Task EditOutline_WhenArchivingAChapterAndAnAct_ShouldTakeThemOutOfTheNumbering()
    {
        var body = await Client.OkAsync("edit_outline", new
        {
            book = Book, ops = new object[] { new { op = "archive", item = Ch3 }, new { op = "archive", item = Practices } },
        });

        _archiveUndo = body.GetProperty("undo_id").GetString()!;
        Renumbered(body).ShouldContain((SeedIds.Chapters.Ch4, "Ch 4", "Ch 3"));
        (await ChapterNumberAsync(SeedIds.Chapters.Ch3)).ShouldBeNull();
        (await ChapterNumberAsync(SeedIds.Chapters.Ch4)).ShouldBe(3);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch8)).ShouldBeNull();
    }

    [Test]
    [Order(5)]
    public async Task EditOutline_WhenRestoringTheArchivedChapterAndAct_ShouldPutThemBackInPlace()
    {
        await Client.OkAsync("get_outline", new { book = Book });

        var body = await Client.OkAsync("edit_outline", new
        {
            book = Book, ops = new object[] { new { op = "restore", item = Ch3 }, new { op = "restore", item = Practices } },
        });

        _restoreUndo = body.GetProperty("undo_id").GetString()!;
        Renumbered(body).ShouldContain((SeedIds.Chapters.Ch4, "Ch 3", "Ch 4"));
        (await ChapterNumberAsync(SeedIds.Chapters.Ch3)).ShouldBe(3);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch8)).ShouldBe(8);
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenGivenTheRestoreThenTheArchive_ShouldEndWhereTheBookStarted()
    {
        await Client.OkAsync("undo", new { undo_id = _restoreUndo });

        (await ChapterNumberAsync(SeedIds.Chapters.Ch3)).ShouldBeNull();
        (await ChapterNumberAsync(SeedIds.Chapters.Ch8)).ShouldBeNull();

        await Client.OkAsync("undo", new { undo_id = _archiveUndo });

        (await ChapterNumberAsync(SeedIds.Chapters.Ch3)).ShouldBe(3);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch4)).ShouldBe(4);
        (await ChapterNumberAsync(SeedIds.Chapters.Ch8)).ShouldBe(8);
    }

    [Test]
    [Order(7)]
    public async Task EditOutline_WhenTheBatchWouldArchiveEveryAct_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book, dry_run = true,
            ops = new object[]
            {
                new { op = "archive", item = SeedIds.Acts.Introduction.ToString() },
                new { op = "archive", item = SeedIds.Acts.TheProductEngineer.ToString() },
                new { op = "archive", item = Practices },
            },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(8)]
    public async Task EditOutline_WhenAnAddedChapterHasLabels_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book, dry_run = true,
            ops = new object[] { new { op = "add", kind = "chapter", into = Practices, labels = new[] { "Ready to write" } } },
        });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(9)]
    public async Task EditOutline_WhenRestoringAnIdThatIsNotArchived_ShouldBeNotFound()
    {
        var error = await Client.ErrorAsync("edit_outline", new { book = Book, ops = new object[] { new { op = "restore", item = Ch7 } } });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }

    private static List<(Guid, string, string)> Renumbered(System.Text.Json.JsonElement body) =>
        body.GetProperty("renumbered").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetGuid(), r.GetProperty("from").GetString()!, r.GetProperty("to").GetString()!)).ToList();

    private async Task<int?> ChapterNumberAsync(Guid chapterId)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int?>("SELECT chapter_number::int FROM chapter_outline WHERE id = @chapterId", new { chapterId });
    }

    private async Task<int?> ChapterNumberAsync(string title)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int?>("SELECT chapter_number::int FROM chapter_outline WHERE title = @title", new { title });
    }

    private async Task<int> LiveCountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(sql);
    }
}
