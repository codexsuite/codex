using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// One scenario: preview the small fixture folder, import it, read what landed, then Undo import.
[TestFixture]
public partial class MarkdownImportTests : ApiFixture
{
    private ImportSummaryDto _summary = null!;

    private object Ids => new { _summary.BookId, _summary.ImportId };

    [Test, Order(1)]
    public async Task Preview_WhenSmallFolder_ShouldListWhatWillBeCreatedAndWriteNothing()
    {
        var before = await BookCountAsync();

        var response = await Client.PostAsync("/api/imports/preview", ImportForms.Form(ImportForms.Folder(ImportForms.SmallFolder)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        preview.Source.ShouldBe("markdown");
        preview.BookTitle.ShouldBe("The Lantern Road");
        preview.FileCount.ShouldBe(14);
        preview.Creates.Select(c => (c.Item, c.Count)).ShouldBe([
            ("Acts", 2), ("Chapters", 3), ("Scenes", 3), ("Archived scenes", 1), ("Scene beats", 6),
            ("Scenes with prose", 2), ("Knowledge base entries", 3), ("Style guide", 1),
        ]);
        preview.Skipped.ShouldBeEmpty();
        preview.Words.ShouldBeGreaterThan(40);
        (await BookCountAsync()).ShouldBe(before);
    }

    [Test, Order(2)]
    public async Task Import_WhenSmallFolder_ShouldAnswer201WithTheSummary()
    {
        var response = await Client.PostAsync("/api/imports", ImportForms.Form(ImportForms.Folder(ImportForms.SmallFolder)));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        _summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        (_summary.Source, _summary.FileName, _summary.Acts, _summary.Chapters, _summary.Scenes, _summary.Entries)
            .ShouldBe(("markdown", "markdown-small", 2, 3, 3, 3));
        _summary.Skipped.ShouldBeEmpty();
        await using var db = await OpenDatabaseAsync();
        _summary.Words.ShouldBe(await db.ExecuteScalarAsync<int>(
            "SELECT sum(p.word_count)::int FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @BookId", Ids));
        (await db.QuerySingleAsync<(string Status, string Source)>(
            "SELECT status, source FROM book_import WHERE id = @ImportId", Ids)).ShouldBe(("done", "markdown"));
    }

    [Test, Order(3)]
    public async Task Import_WhenSmallFolder_ShouldMapBookActsAndChapters()
    {
        await using var db = await OpenDatabaseAsync();
        var book = await db.QuerySingleAsync<(string Title, string? Author, string? Description, string? StyleGuide)>(
            "SELECT title, author_name, description, style_guide FROM book WHERE id = @BookId", Ids);
        book.Title.ShouldBe("The Lantern Road");
        book.Author.ShouldBe("J. Dickson");
        book.Description.ShouldBe("A courier carries a lantern that must never go out.");
        book.StyleGuide.ShouldBe("# Writing rules\n\nWrite in close third person. Keep sentences short.\n\n# Memory\n\nMara's brother is called Tobin.");

        var acts = (await db.QueryAsync<(string Position, string Title, string? Summary)>(
            "SELECT position, title, summary FROM act WHERE book_id = @BookId ORDER BY position", Ids)).ToList();
        acts.ShouldBe([("a0", "The Call", "Mara is asked to carry the lantern."), (acts[1].Position, "The Road", "The journey north.")]);

        var chapters = (await db.QueryAsync<(string Title, string? Summary, string? Notes, bool IsNumbered)>(
            """
            SELECT c.title, c.summary, c.notes, c.is_numbered FROM chapter c JOIN act a ON a.id = c.act_id
            WHERE c.book_id = @BookId ORDER BY a.position, c.position
            """, Ids)).ToList();
        chapters.Select(c => (c.Title, c.IsNumbered)).ShouldBe([("Preface", false), ("A Letter Arrives", true), ("Crossing", true)]);
        chapters[1].Summary.ShouldBe("The letter reaches Mara and she refuses, then accepts.");
        chapters[1].Notes.ShouldBe("## Decisions\n\n- The letter is unsigned.\n\n## Folded\n\n- The market scene (folded into scene 1).");
    }

    [Test, Order(4)]
    public async Task Import_WhenSmallFolder_ShouldMapScenesBeatsAndEntries()
    {
        await using var db = await OpenDatabaseAsync();
        var scenes = (await db.QueryAsync<(string ImportRef, string? Title, string Status, bool Archived, string? PlanNotes)>(
            """
            SELECT s.import_ref, s.title, s.status, s.archived_at IS NOT NULL, s.plan_notes
            FROM scene s JOIN chapter c ON c.id = s.chapter_id JOIN act a ON a.id = c.act_id
            WHERE s.book_id = @BookId ORDER BY a.position, c.position, s.position
            """, Ids)).ToList();
        scenes.Select(s => (s.ImportRef, s.Title, s.Status, s.Archived)).ShouldBe([
            ("ch00-s01", null, "outline", false), ("ch01-s01", "The letter", "draft", false),
            ("ch01-s02", null, "outline", true), ("ch03-s01", null, "beats", false),
        ]);
        scenes[1].PlanNotes.ShouldBe("## Purpose\n\nIntroduce Mara and the letter.\n\n## Covers\n\nThe market, the letter, her refusal.\n\n## Boundaries\n\nNo mention of the brother yet.");

        var beats = (await db.QueryAsync<string>(
            """
            SELECT b.body FROM beat b JOIN scene s ON s.id = b.scene_id JOIN prose p ON p.scene_id = s.id
            WHERE s.import_ref = 'ch01-s01' AND s.book_id = @BookId AND b.archived_at IS NULL ORDER BY strpos(p.body, b.id::text)
            """,
            Ids)).ToList();
        beats.ShouldBe([
            "**Mara at the market.** She haggles over apples and loses.",
            "**The letter.** A boy hands her a letter sealed with wax.",
            "**Refusal.** She tears it in half.",
        ]);

        var links = (await db.QueryAsync<(string ImportRef, string Entry, string Category)>(
            """
            SELECT s.import_ref, e.name, k.name FROM scene_kb sk
            JOIN scene s ON s.id = sk.scene_id JOIN kb_entry e ON e.id = sk.kb_entry_id JOIN kb_category k ON k.id = e.category_id
            WHERE s.book_id = @BookId ORDER BY s.import_ref, e.name
            """, Ids)).ToList();
        links.ShouldBe([
            ("ch01-s01", "Mara", "Imported"), ("ch01-s01", "The Lantern", "Imported"), ("ch01-s02", "Mara", "Imported"),
            ("ch03-s01", "The Lantern", "Imported"), ("ch03-s01", "Tobin", "Imported"),
        ]);
    }

    [TestCase("ch00-s01")]
    [TestCase("ch01-s01")]
    [TestCase("ch03-s01")]
    [Order(5)]
    public async Task Import_WhenSmallFolder_ShouldWriteStoredProseWithBeatsOnTopAndOneImportRevision(string importRef)
    {
        await using var db = await OpenDatabaseAsync();
        var prose = await db.QuerySingleAsync<(Guid SceneId, string Body, int Version)>(
            "SELECT p.scene_id, p.body, p.version FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @bookId AND s.import_ref = @importRef",
            new { bookId = _summary.BookId, importRef });
        var beatIds = (await db.QueryAsync<Guid>("SELECT id FROM beat WHERE scene_id = @SceneId AND archived_at IS NULL", new { prose.SceneId })).ToList();

        var n = 0;
        var inBody = new List<Guid>();
        var normalised = BeatLine().Replace(prose.Body, m =>
        {
            inBody.Add(Guid.Parse(m.Groups[1].Value));
            return $"::beat{{id=\"0199c0de-0000-7000-8000-0000000000b{++n}\"}}";
        });
        inBody.ShouldBe(beatIds, ignoreOrder: true);
        normalised.ShouldBe(ImportForms.ExpectedProse(importRef));
        prose.Version.ShouldBe(1);
        (await db.QueryAsync<(string Source, string Body)>("SELECT source, body FROM prose_revision WHERE scene_id = @SceneId", new { prose.SceneId }))
            .ShouldBe([("import", prose.Body)]);
    }

    [Test, Order(6)]
    public async Task Banner_WhenImported_ShouldReturnTheSummary()
    {
        var banner = (await Client.GetFromJsonAsync<BookImportDto>($"/api/books/{_summary.BookId}/import"))!;

        banner.Banner.ShouldBeEquivalentTo(_summary);
    }

    [Test, Order(7)]
    public async Task Undo_WhenImported_ShouldMarkTheBookDeletedAndHideIt()
    {
        var response = await Client.PostAsync($"/api/imports/{_summary.ImportId}/undo", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            """
            SELECT count(*) FROM book WHERE id = @BookId AND deleted_at IS NOT NULL AND archived_at IS NOT NULL
            """, Ids)).ShouldBe(1);
        (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM book_import WHERE id = @ImportId", Ids)).ShouldBe(1);
        (await Client.GetAsync($"/api/books/{_summary.BookId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/api/books/{_summary.BookId}/import")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(8)]
    public async Task Undo_WhenAlreadyUndone_ShouldAnswer404()
    {
        var response = await Client.PostAsync($"/api/imports/{_summary.ImportId}/undo", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    private async Task<int> BookCountAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book");
    }

    [GeneratedRegex("""^::beat\{id="([0-9a-f-]{36})"\}$""", RegexOptions.Multiline)]
    private static partial Regex BeatLine();
}
