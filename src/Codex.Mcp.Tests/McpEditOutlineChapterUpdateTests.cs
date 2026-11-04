using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Dapper;
using ModelContextProtocol.Client;
using Npgsql;

namespace Codex.Mcp.Tests;

// edit_outline update on chapters and acts (title, summary, plan_notes, is_numbered; an act's title), the §[Chapter] link
// in scene summaries, and undo of each. One scenario on its own clone, in order.
[TestFixture]
[CancelAfter(90000)]
public class McpEditOutlineChapterUpdateTests
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Ch3 = SeedIds.Chapters.Ch3.ToString();
    private static readonly string Ch7 = SeedIds.Chapters.Ch7.ToString();
    private static readonly string Practices = SeedIds.Acts.Practices.ToString();
    private McpStdioServer _server = null!;
    private string _slug = "";
    private string _originalTitle = "";
    private string _renameUndo = "";
    private string _unnumberUndo = "";
    private string _actUndo = "";
    private string _notesUndo = "";

    private McpClient Client => _server.Client;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _server = await McpStdioServer.StartAsync();
        await using var connection = Open();
        (_slug, _originalTitle) = await connection.QuerySingleAsync<(string, string)>(
            "SELECT slug, title FROM chapter WHERE id = @id", new { id = SeedIds.Chapters.Ch3 });
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    [Order(1)]
    public async Task EditOutline_WhenRenamingAChapterAsADryRun_ShouldWriteNothing()
    {
        await Client.OkAsync("edit_outline", new
        {
            book = Book, dry_run = true,
            ops = new object[] { new { op = "update", item = Ch3, title = "Dry title", plan_notes = "Dry notes" } },
        });

        (await ChapterAsync(SeedIds.Chapters.Ch3)).Title.ShouldBe(_originalTitle);
    }

    [Test]
    [Order(2)]
    public async Task EditOutline_WhenUpdatingAChapterTitleSummaryAndPlanNotes_ShouldWriteThemAndKeepTheSlug()
    {
        var body = await Client.OkAsync("edit_outline", new
        {
            book = Book,
            ops = new object[] { new { op = "update", item = Ch3, title = "  Renamed by the agent ", summary = "The shape of it.", plan_notes = "Covers X; not Y." } },
        });

        _renameUndo = body.GetProperty("undo_id").GetString()!;
        var chapter = await ChapterAsync(SeedIds.Chapters.Ch3);
        (chapter.Title, chapter.Summary, chapter.Notes, chapter.Slug).ShouldBe(("Renamed by the agent", "The shape of it.", "Covers X; not Y.", _slug));
        body.GetProperty("renumbered").GetArrayLength().ShouldBe(0);
    }

    [Test]
    [Order(3)]
    public async Task EditOutline_WhenAChapterWasRenamed_ShouldStillResolveItBySlug()
    {
        var body = await Client.OkAsync("edit_outline", new
        {
            book = Book, dry_run = true,
            ops = new object[] { new { op = "update", item = $"ch:{_slug}", summary = "Found by slug." } },
        });

        body.GetProperty("results")[0].GetProperty("item").GetProperty("id").GetGuid().ShouldBe(SeedIds.Chapters.Ch3);
    }

    [Test]
    [Order(4)]
    public async Task Undo_WhenTheChapterWasUpdated_ShouldPutTheOldFieldsBack()
    {
        await Client.OkAsync("undo", new { undo_id = _renameUndo });

        var chapter = await ChapterAsync(SeedIds.Chapters.Ch3);
        (chapter.Title, chapter.Summary, chapter.Notes, chapter.Slug).ShouldBe((_originalTitle, null, null, _slug));
    }

    [Test]
    [Order(5)]
    public async Task EditOutline_WhenTakingAChapterOutOfTheNumbering_ShouldRenumberTheOnesAfterIt()
    {
        var body = await Client.OkAsync("edit_outline", new
        {
            book = Book,
            ops = new object[] { new { op = "update", item = Ch3, is_numbered = false } },
        });

        _unnumberUndo = body.GetProperty("undo_id").GetString()!;
        var renumbered = body.GetProperty("renumbered").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetGuid(), r.GetProperty("from").GetString()!, r.GetProperty("to").GetString()!)).ToList();
        renumbered.ShouldContain((SeedIds.Chapters.Ch3, "Ch 3", "unnumbered"));
        renumbered.ShouldContain((SeedIds.Chapters.Ch4, "Ch 4", "Ch 3"));
        (await NumberAsync(SeedIds.Chapters.Ch3)).ShouldBeNull();
        (await NumberAsync(SeedIds.Chapters.Ch4)).ShouldBe(3);
    }

    [Test]
    [Order(6)]
    public async Task Undo_WhenTheChapterWasTakenOutOfTheNumbering_ShouldNumberItAgain()
    {
        await Client.OkAsync("undo", new { undo_id = _unnumberUndo });

        (await NumberAsync(SeedIds.Chapters.Ch3)).ShouldBe(3);
        (await NumberAsync(SeedIds.Chapters.Ch4)).ShouldBe(4);
    }

    [Test]
    [Order(7)]
    public async Task EditOutline_WhenRenamingAnAct_ShouldRenameItAndUndoPutsTheOldTitleBack()
    {
        await using var connection = Open();
        var before = await connection.ExecuteScalarAsync<string>("SELECT title FROM act WHERE id = @id", new { id = SeedIds.Acts.Practices });

        var body = await Client.OkAsync("edit_outline", new { book = Book, ops = new object[] { new { op = "update", item = Practices, title = "Craft" } } });
        _actUndo = body.GetProperty("undo_id").GetString()!;
        (await connection.ExecuteScalarAsync<string>("SELECT title FROM act WHERE id = @id", new { id = SeedIds.Acts.Practices })).ShouldBe("Craft");

        await Client.OkAsync("undo", new { undo_id = _actUndo });
        (await connection.ExecuteScalarAsync<string>("SELECT title FROM act WHERE id = @id", new { id = SeedIds.Acts.Practices })).ShouldBe(before);
    }

    [Test]
    [Order(8)]
    public async Task Undo_WhenTheChapterWasEditedAfterTheWrite_ShouldBeAConflictAndChangeNothing()
    {
        var body = await Client.OkAsync("edit_outline", new { book = Book, ops = new object[] { new { op = "update", item = Ch3, title = "First" } } });
        await using var connection = Open();
        await connection.ExecuteAsync("UPDATE chapter SET title = 'Author edit' WHERE id = @id", new { id = SeedIds.Chapters.Ch3 });

        var error = await Client.ErrorAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await ChapterAsync(SeedIds.Chapters.Ch3)).Title.ShouldBe("Author edit");
        await connection.ExecuteAsync("UPDATE chapter SET title = @title WHERE id = @id", new { id = SeedIds.Chapters.Ch3, title = _originalTitle });
    }

    [TestCase("chapter", "status", "outline")]
    [TestCase("chapter", "add_labels", "Drafting")]
    [TestCase("act", "summary", "A summary")]
    [TestCase("act", "plan_notes", "Notes")]
    [TestCase("act", "is_numbered", false)]
    [TestCase("scene", "is_numbered", false)]
    [Order(9)]
    public async Task EditOutline_WhenAFieldDoesNotApplyToTheKind_ShouldBeInvalidNamingTheField(string kind, string name, object value)
    {
        var item = kind switch { "chapter" => Ch7, "act" => Practices, _ => "4.1" };
        var op = new Dictionary<string, object> { ["op"] = "update", ["item"] = item, [name] = name == "add_labels" ? new[] { value } : value };

        var error = await Client.ErrorAsync("edit_outline", new { book = Book, dry_run = true, ops = new object[] { op } });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe($"ops[0].{name}");
    }

    [Test]
    [Order(10)]
    public async Task EditOutline_WhenAChapterUpdateGivesNothingToChange_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("edit_outline", new { book = Book, dry_run = true, ops = new object[] { new { op = "update", item = Ch7 } } });

        error.GetProperty("code").GetString().ShouldBe("invalid");
    }

    [Test]
    [Order(11)]
    public async Task EditOutline_WhenAChapterTitleIsEmpty_ShouldBeInvalid()
    {
        var error = await Client.ErrorAsync("edit_outline", new { book = Book, dry_run = true, ops = new object[] { new { op = "update", item = Ch7, title = "  " } } });

        error.GetProperty("code").GetString().ShouldBe("invalid");
        error.GetProperty("field").GetString().ShouldBe("ops[0].title");
    }

    [Test]
    [Order(12)]
    public async Task EditOutline_WhenALaterOpFails_ShouldRollBackAChapterUpdateInTheSameBatch()
    {
        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book,
            ops = new object[]
            {
                new { op = "update", item = Ch3, title = "Should not stick" },
                new { op = "update", item = "4.1", summary = "Points at §[No such chapter]." },
            },
        });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        (await ChapterAsync(SeedIds.Chapters.Ch3)).Title.ShouldBe(_originalTitle);
    }

    [Test]
    [Order(13)]
    public async Task EditOutline_WhenASummaryHasAChapterLink_ShouldStoreTheChapterIdLink()
    {
        await using var connection = Open();
        var title = await connection.ExecuteScalarAsync<string>("SELECT title FROM chapter WHERE id = @id", new { id = SeedIds.Chapters.Ch7 });

        await Client.OkAsync("edit_outline", new
        {
            book = Book,
            ops = new object[] { new { op = "update", item = "4.1", summary = $"Picks up from §[{title}]." } },
        });

        var summary = await connection.ExecuteScalarAsync<string>(
            "SELECT summary FROM scene WHERE chapter_id = @id AND archived_at IS NULL ORDER BY position LIMIT 1", new { id = SeedIds.Chapters.Ch4 });
        summary.ShouldBe($"Picks up from [{title}](chapter:{SeedIds.Chapters.Ch7}).");
    }

    [Test]
    [Order(14)]
    public async Task EditOutline_WhenAChapterLinkNamesTwoChapters_ShouldBeAmbiguousAndWriteNothing()
    {
        await using var connection = Open();
        await connection.ExecuteAsync(
            """
            INSERT INTO chapter (book_id, act_id, position, title)
            SELECT book_id, act_id, position || 'z', title FROM chapter WHERE id = @id
            """, new { id = SeedIds.Chapters.Ch7 });
        var title = await connection.ExecuteScalarAsync<string>("SELECT title FROM chapter WHERE id = @id", new { id = SeedIds.Chapters.Ch7 });

        var error = await Client.ErrorAsync("edit_outline", new
        {
            book = Book, ops = new object[] { new { op = "update", item = "4.1", summary = $"See §[{title}]." } },
        });

        error.GetProperty("code").GetString().ShouldBe("ambiguous");
    }

    [Test]
    [Order(15)]
    public async Task EditOutline_WhenUpdatingASceneWithPlanNotes_ShouldResolveLinksWriteThemAndShowThemInGetOutline()
    {
        await using var connection = Open();
        var title = await connection.ExecuteScalarAsync<string>("SELECT title FROM chapter WHERE id = @id", new { id = SeedIds.Chapters.Ch8 });
        var dry = await Client.OkAsync("edit_outline", new
        {
            book = Book, dry_run = true, ops = new object[] { new { op = "update", item = "4.1", plan_notes = "Purpose: x" } },
        });
        dry.GetProperty("dry_run").GetBoolean().ShouldBeTrue();
        (await SceneNotesAsync()).ShouldBeNull();

        var body = await Client.OkAsync("edit_outline", new
        {
            book = Book, ops = new object[] { new { op = "update", item = "4.1", plan_notes = $"Covers §[{title}]." } },
        });
        _notesUndo = body.GetProperty("undo_id").GetString()!;

        (await SceneNotesAsync()).ShouldBe($"Covers [{title}](chapter:{SeedIds.Chapters.Ch8}).");
        var outline = await Client.OkAsync("get_outline", new { book = Book, within = "4", include = new[] { "plan_notes" } });
        outline.GetProperty("acts")[0].GetProperty("chapters")[0].GetProperty("scenes")[0].GetProperty("plan_notes").GetString()
            .ShouldBe($"Covers [{title}](chapter:{SeedIds.Chapters.Ch8}).");
    }

    [Test]
    [Order(16)]
    public async Task Undo_WhenAScenePlanNotesWereEditedAfterTheWrite_ShouldBeAConflict()
    {
        await using var connection = Open();
        await connection.ExecuteAsync("UPDATE scene SET plan_notes = 'Author notes' WHERE id = (SELECT id FROM scene WHERE chapter_id = @id AND archived_at IS NULL ORDER BY position LIMIT 1)", new { id = SeedIds.Chapters.Ch4 });

        var error = await Client.ErrorAsync("undo", new { undo_id = _notesUndo });

        error.GetProperty("code").GetString().ShouldBe("conflict");
        (await SceneNotesAsync()).ShouldBe("Author notes");
    }

    [Test]
    [Order(17)]
    public async Task Undo_WhenAScenePlanNotesAreUntouched_ShouldClearThem()
    {
        await using var connection = Open();
        await connection.ExecuteAsync("UPDATE scene SET plan_notes = NULL WHERE chapter_id = @id", new { id = SeedIds.Chapters.Ch4 });
        var body = await Client.OkAsync("edit_outline", new { book = Book, ops = new object[] { new { op = "update", item = "4.1", plan_notes = "Fresh" } } });
        (await SceneNotesAsync()).ShouldBe("Fresh");

        await Client.OkAsync("undo", new { undo_id = body.GetProperty("undo_id").GetString() });

        (await SceneNotesAsync()).ShouldBeNull();
    }

    private async Task<string?> SceneNotesAsync()
    {
        await using var connection = Open();
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT plan_notes FROM scene WHERE chapter_id = @id AND archived_at IS NULL ORDER BY position LIMIT 1", new { id = SeedIds.Chapters.Ch4 });
    }

    private NpgsqlConnection Open() => new(_server.ConnectionString);

    private sealed record ChapterRow(string Title, string? Summary, string? Notes, string Slug);

    private async Task<ChapterRow> ChapterAsync(Guid id)
    {
        await using var connection = Open();
        return await connection.QuerySingleAsync<ChapterRow>("SELECT title, summary, notes, slug FROM chapter WHERE id = @id", new { id });
    }

    private async Task<int?> NumberAsync(Guid id)
    {
        await using var connection = Open();
        return await connection.ExecuteScalarAsync<int?>("SELECT chapter_number::int FROM chapter_outline WHERE id = @id", new { id });
    }
}
