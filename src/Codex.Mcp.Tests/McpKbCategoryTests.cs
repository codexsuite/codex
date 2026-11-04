using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// manage_kb_category (#78) on the Product Engineering seed: create, rename, reorder and delete (empty, and with
// move_to), dry_run, the conflict cases, and undo of each. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(90000)]
public class McpKbCategoryTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private McpStdioServer _server = null!;
    private Guid _scratch;
    private string _createUndo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task ManageKbCategory_WhenCreating_ShouldAddItLastWithTheColour()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "create", category = "Scratch", colour = "category-law" });

        _scratch = Guid.Parse(result.GetProperty("item").GetProperty("id").GetString()!);
        _createUndo = result.GetProperty("undo_id").GetString()!;
        (await NamesAsync())[^1].ShouldBe("Scratch");
        (await ColourAsync(_scratch)).ShouldBe("category-law");
    }

    [Test]
    [Order(2)]
    public async Task ManageKbCategory_WhenTheNameExists_ShouldBeConflictAndWriteNothing()
    {
        var error = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "create", category = "laws" });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await NamesAsync()).Count(n => n == "Scratch").ShouldBe(1);
    }

    [Test]
    [Order(3)]
    public async Task ManageKbCategory_WhenRenamingWithDryRun_ShouldReportAndWriteNothing()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "rename", category = "Scratch", name = "Notes", dry_run = true });

        result.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        result.TryGetProperty("undo_id", out _).ShouldBeFalse();
        (await NamesAsync()).ShouldContain("Scratch");
    }

    [Test]
    [Order(4)]
    public async Task ManageKbCategory_WhenRenamingToAnExistingName_ShouldBeConflict()
    {
        var error = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "rename", category = "Scratch", name = "Research" });

        error.GetProperty("code").GetString().ShouldBe("conflict");
    }

    [Test]
    [Order(5)]
    public async Task ManageKbCategory_WhenRenamingAndRecolouring_ShouldChangeBothAndUndoShouldPutThemBack()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "rename", category = "Scratch", name = "Notes", colour = "category-story" });

        (await NamesAsync()).ShouldContain("Notes");
        (await ColourAsync(_scratch)).ShouldBe("category-story");
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await NamesAsync()).ShouldContain("Scratch");
        (await ColourAsync(_scratch)).ShouldBe("category-law");
    }

    [Test]
    [Order(6)]
    public async Task ManageKbCategory_WhenTheNameAlreadyReads_ShouldChangeNothingAndRecordNoUndo()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "rename", category = "Scratch", name = "Scratch" });

        result.TryGetProperty("undo_id", out _).ShouldBeFalse();
    }

    [Test]
    [Order(7)]
    public async Task Undo_WhenARenamedCategoryWasRenamedAgain_ShouldBeConflict()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "rename", category = "Scratch", name = "Notes" });
        await ExecuteAsync("UPDATE kb_category SET name = 'Elsewhere' WHERE id = @id", new { id = _scratch });

        var error = await Client.ErrorAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await NamesAsync()).ShouldContain("Elsewhere");
        await ExecuteAsync("UPDATE kb_category SET name = 'Scratch' WHERE id = @id", new { id = _scratch });
    }

    [Test]
    [Order(8)]
    public async Task ManageKbCategory_WhenReorderingFirst_ShouldMoveItAndUndoShouldPutItBack()
    {
        var before = await NamesAsync();

        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", position = "first" });

        (await NamesAsync()).ShouldBe(["Scratch", .. before.Where(n => n != "Scratch")]);
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await NamesAsync()).ShouldBe(before);
    }

    [Test]
    [Order(9)]
    public async Task ManageKbCategory_WhenReorderingBeforeAndAfter_ShouldPlaceItNextToTheOther()
    {
        var before = await NamesAsync();

        var beforeResult = await Client.OkAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", before = "Laws" });
        var afterBefore = await NamesAsync();
        var afterResult = await Client.OkAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", after = "Stories" });
        var afterAfter = await NamesAsync();

        afterBefore.ShouldBe(["Blog Posts", "Scratch", "Laws", "Stories", "Research", "People"]);
        afterAfter.ShouldBe(["Blog Posts", "Laws", "Stories", "Scratch", "Research", "People"]);
        await Client.OkAsync("undo", new { undo_id = afterResult.GetProperty("undo_id").GetString() });
        (await NamesAsync()).ShouldBe(afterBefore);
        await Client.OkAsync("undo", new { undo_id = beforeResult.GetProperty("undo_id").GetString() });
        (await NamesAsync()).ShouldBe(before);
    }

    [Test]
    [Order(10)]
    public async Task ManageKbCategory_WhenAlreadyThere_ShouldChangeNothingAndRecordNoUndo()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", position = "last" });

        result.TryGetProperty("undo_id", out _).ShouldBeFalse();
    }

    [Test]
    [Order(11)]
    public async Task ManageKbCategory_WhenReorderIsAmbiguousOrAnchorIsMissing_ShouldFail()
    {
        var both = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", before = "Laws", position = "first" });
        var none = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch" });
        var unknown = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", before = "Nope" });
        var itself = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", after = "Scratch" });

        both.GetProperty("code").GetString().ShouldBe("invalid");
        none.GetProperty("code").GetString().ShouldBe("invalid");
        unknown.GetProperty("code").GetString().ShouldBe("not_found");
        itself.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(12)]
    public async Task Undo_WhenAReorderedCategoryWasMovedAgain_ShouldBeConflict()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", position = "first" });
        await ExecuteAsync("UPDATE kb_category SET position = 'zz' WHERE id = @id", new { id = _scratch });

        var error = await Client.ErrorAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        await Client.OkAsync("manage_kb_category", new { book = Book, op = "reorder", category = "Scratch", position = "last" });
    }

    [Test]
    [Order(13)]
    public async Task ManageKbCategory_WhenDeletingACategoryWithLiveEntries_ShouldBeConflictNamingTheCount()
    {
        var error = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "delete", category = "Laws" });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        error.GetProperty("message").GetString()!.ShouldContain("3 live entries");
        (await NamesAsync()).ShouldContain("Laws");
    }

    [Test]
    [Order(14)]
    public async Task ManageKbCategory_WhenDeletingAnEmptyCategory_ShouldArchiveItAndUndoShouldBringItBackWithTheSameId()
    {
        await ExecuteAsync("UPDATE kb_category SET icon = 'star', field_defs = '[{\"key\":\"born\",\"label\":\"Born\"}]' WHERE id = @id", new { id = _scratch });
        var positionBefore = await PositionAsync(_scratch);

        var dry = await Client.OkAsync("manage_kb_category", new { book = Book, op = "delete", category = "Scratch", dry_run = true });
        (await NamesAsync()).ShouldContain("Scratch");
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "delete", category = "Scratch" });

        dry.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        (await NamesAsync()).ShouldNotContain("Scratch");
        (await CategoryArchivedAsync(_scratch)).ShouldBe(true);
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await CategoryArchivedAsync(_scratch)).ShouldBe(false);
        (await NamesAsync())[^1].ShouldBe("Scratch");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        var row = await connection.QuerySingleAsync<(string Icon, string Defs, string Position, string Colour)>(
            "SELECT icon, field_defs::text, position, color FROM kb_category WHERE id = @id", new { id = _scratch });
        row.Icon.ShouldBe("star");
        row.Defs.ShouldContain("\"born\"");
        row.Position.ShouldBe(positionBefore);
        row.Colour.ShouldBe("category-law");
    }

    [Test]
    [Order(15)]
    public async Task Undo_WhenTheDeletedNameWasTakenAgain_ShouldBeConflict()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "delete", category = "Scratch" });
        await Client.OkAsync("manage_kb_category", new { book = Book, op = "create", category = "Scratch" });

        var error = await Client.ErrorAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
    }

    [Test]
    [Order(16)]
    public async Task Undo_WhenACreatedCategoryHasEntriesNow_ShouldBeConflictAndWhenEmptyShouldArchiveIt()
    {
        // The re-created Scratch (order 15) holds an entry, so the first create's undo id no longer applies; make a fresh pair.
        var made = await Client.OkAsync("manage_kb_category", new { book = Book, op = "create", category = "Holding" });
        var holding = Guid.Parse(made.GetProperty("item").GetProperty("id").GetString()!);
        var entry = await Client.OkAsync("upsert_kb_entry", new { book = Book, name = "Held Thing", category = "Holding" });

        var blocked = await Client.ErrorAsync("undo", new { undo_id = made.GetProperty("undo_id").GetString() });
        await Client.OkAsync("undo", new { undo_id = entry.GetProperty("undo_id").GetString() });
        await Client.OkAsync("undo", new { undo_id = made.GetProperty("undo_id").GetString() });

        blocked.GetProperty("code").GetString().ShouldBe("conflict");
        (await NamesAsync()).ShouldNotContain("Holding");
        (await CategoryArchivedAsync(holding)).ShouldBe(true);
    }

    [Test]
    [Order(17)]
    public async Task ManageKbCategory_WhenDeletingWithMoveTo_ShouldMoveTheEntriesInOrderAndUndoShouldMoveThemBack()
    {
        var temp = await Client.OkAsync("manage_kb_category", new { book = Book, op = "create", category = "Temp" });
        var tempId = Guid.Parse(temp.GetProperty("item").GetProperty("id").GetString()!);
        await Client.OkAsync("upsert_kb_entry", new { book = Book, name = "Temp One", category = "Temp" });
        await Client.OkAsync("upsert_kb_entry", new { book = Book, name = "Temp Two", category = "Temp" });
        var researchBefore = await EntriesInAsync("Research");

        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "delete", category = "Temp", move_to = "Research" });

        result.GetProperty("moved_entries").GetInt32().ShouldBe(2);
        (await NamesAsync()).ShouldNotContain("Temp");
        (await CategoryArchivedAsync(tempId)).ShouldBe(true);
        (await EntriesInAsync("Research")).ShouldBe([.. researchBefore, "Temp One", "Temp Two"]);
        await Client.OkAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });
        (await CategoryArchivedAsync(tempId)).ShouldBe(false);
        (await EntriesInAsync("Temp")).ShouldBe(["Temp One", "Temp Two"]);
        (await EntriesInAsync("Research")).ShouldBe(researchBefore);
    }

    [Test]
    [Order(18)]
    public async Task ManageKbCategory_WhenMoveToIsUnknownOrTheSameCategory_ShouldFailAndWriteNothing()
    {
        var unknown = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "delete", category = "Temp", move_to = "Nope" });
        var same = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "delete", category = "Temp", move_to = "temp" });

        unknown.GetProperty("code").GetString().ShouldBe("not_found");
        same.GetProperty("code").GetString().ShouldBe("invalid");
        (await NamesAsync()).ShouldContain("Temp");
    }

    [Test]
    [Order(19)]
    public async Task Undo_WhenAMovedEntryWasMovedAgain_ShouldBeConflict()
    {
        var result = await Client.OkAsync("manage_kb_category", new { book = Book, op = "delete", category = "Temp", move_to = "Research" });
        await ExecuteAsync(
            "UPDATE kb_entry SET category_id = (SELECT id FROM kb_category WHERE book_id = @book AND name = 'People') WHERE name = 'Temp One'",
            new { book = SeedIds.Books.ProductEngineering });

        var error = await Client.ErrorAsync("undo", new { undo_id = result.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await NamesAsync()).ShouldNotContain("Temp");
    }

    [Test]
    [Order(20)]
    public async Task ManageKbCategory_WhenOpIsUnknown_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("manage_kb_category", new { book = Book, op = "merge", category = "Laws" });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        _createUndo.ShouldNotBeEmpty();
    }

    [Test]
    [Order(21)]
    public async Task ManageKbCategory_WhenACategoryWithADeletedEntryIsDeleted_ShouldKeepTheEntryArchivedAndRestorableAndFreeTheName()
    {
        var made = await Client.OkAsync("manage_kb_category", new { book = Book, op = "create", category = "Doomed" });
        var doomed = Guid.Parse(made.GetProperty("item").GetProperty("id").GetString()!);
        var entry = await Client.OkAsync("upsert_kb_entry", new { book = Book, name = "Ghost Thing", category = "Doomed" });
        var entryId = Guid.Parse(entry.GetProperty("item").GetProperty("id").GetString()!);
        var entryDelete = await Client.OkAsync("delete_kb_entry", new { entry = entryId.ToString() });

        var deleted = await Client.OkAsync("manage_kb_category", new { book = Book, op = "delete", category = "Doomed" });

        (await CategoryArchivedAsync(doomed)).ShouldBe(true);
        (await EntryArchivedAsync(entryId)).ShouldBe(true);
        (await NamesAsync()).ShouldNotContain("Doomed");
        (await Client.ErrorAsync("list_kb_entries", new { book = Book, category = "Doomed" })).GetProperty("code").GetString().ShouldBe("not_found");
        // The name is free again, and an entry cannot be put into the deleted category.
        var again = await Client.OkAsync("manage_kb_category", new { book = Book, op = "create", category = "Doomed" });
        again.GetProperty("item").GetProperty("id").GetString().ShouldNotBe(doomed.ToString());
        var restoreBlocked = await Client.ErrorAsync("undo", new { undo_id = entryDelete.GetProperty("undo_id").GetString() });
        restoreBlocked.GetProperty("code").GetString().ShouldBe("conflict");
        // Free the name, bring the category back, and the entry's own undo works again.
        await Client.OkAsync("undo", new { undo_id = again.GetProperty("undo_id").GetString() });
        await Client.OkAsync("undo", new { undo_id = deleted.GetProperty("undo_id").GetString() });
        await Client.OkAsync("undo", new { undo_id = entryDelete.GetProperty("undo_id").GetString() });
        (await EntryArchivedAsync(entryId)).ShouldBe(false);
        (await EntriesInAsync("Doomed")).ShouldBe(["Ghost Thing"]);
    }

    private async Task<bool?> EntryArchivedAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<bool?>("SELECT archived_at IS NOT NULL FROM kb_entry WHERE id = @id", new { id });
    }

    private async Task<List<string>> NamesAsync()
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return (await connection.QueryAsync<string>(
            "SELECT name FROM kb_category WHERE book_id = @book AND archived_at IS NULL ORDER BY position COLLATE \"C\", id",
            new { book = SeedIds.Books.ProductEngineering })).ToList();
    }

    private async Task<List<string>> EntriesInAsync(string category)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return (await connection.QueryAsync<string>(
            """
            SELECT e.name FROM kb_entry e JOIN kb_category c ON c.id = e.category_id
            WHERE c.book_id = @book AND c.name = @category AND c.archived_at IS NULL AND e.archived_at IS NULL ORDER BY e.position COLLATE "C", e.id
            """,
            new { book = SeedIds.Books.ProductEngineering, category })).ToList();
    }

    private async Task<string> ColourAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<string>("SELECT color FROM kb_category WHERE id = @id", new { id });
    }

    private async Task<string> PositionAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.QuerySingleAsync<string>("SELECT position FROM kb_category WHERE id = @id", new { id });
    }

    // Soft delete: the row is never removed, so "gone" means archived. Null when there is no such row at all.
    private async Task<bool?> CategoryArchivedAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        return await connection.ExecuteScalarAsync<bool?>("SELECT archived_at IS NOT NULL FROM kb_category WHERE id = @id", new { id });
    }

    private async Task ExecuteAsync(string sql, object arguments)
    {
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        await connection.ExecuteAsync(sql, arguments);
    }
}
