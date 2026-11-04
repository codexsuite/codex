using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class OutlineEndpointTests : ApiFixture
{
    private Guid _bookId;
    private Guid _sceneOne;
    private Guid _labelId;
    private Guid _entryId;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        var actOne = await TestRows.FirstActAsync(db, _bookId);
        var actTwo = await TestRows.ActAsync(db, _bookId, "a1", "Part two");

        // Inserted out of reading order on purpose.
        var chapterTwo = await TestRows.ChapterAsync(db, _bookId, actTwo, "a0", "Second");
        var chapterOne = await TestRows.ChapterAsync(db, _bookId, actOne, "a1", "First");
        var preface = await TestRows.ChapterAsync(db, _bookId, actOne, "a0", "Preface", isNumbered: false);
        var archivedChapter = await TestRows.ChapterAsync(db, _bookId, actTwo, "a1", "Cut chapter");
        await db.ExecuteAsync("UPDATE chapter SET archived_at = now() WHERE id = @archivedChapter", new { archivedChapter });

        await TestRows.SceneAsync(db, _bookId, chapterTwo, "a0", "Twist");
        var sceneTwo = await TestRows.SceneAsync(db, _bookId, chapterOne, "a1", "Middle", status: "draft");
        _sceneOne = await TestRows.SceneAsync(db, _bookId, chapterOne, "a0", "Opening", "The **hook**.");
        await TestRows.SceneAsync(db, _bookId, chapterOne, "a2", "Folded", archived: true);
        await TestRows.SceneAsync(db, _bookId, preface, "a0", "Epigraph");

        await TestRows.ProseAsync(db, sceneTwo, "one two three four five");
        _labelId = await TestRows.LabelAsync(db, _bookId, "Flashback", color: "category-amber");
        await db.ExecuteAsync("INSERT INTO scene_label (scene_id, label_id) VALUES (@sceneId, @labelId)", new { sceneId = _sceneOne, labelId = _labelId });
        var categoryId = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO kb_category (book_id, name, position) VALUES (@bookId, 'People', 'a0') RETURNING id", new { bookId = _bookId });
        _entryId = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO kb_entry (book_id, category_id, name, position) VALUES (@bookId, @categoryId, 'Guyon', 'a0') RETURNING id",
            new { bookId = _bookId, categoryId });
        await db.ExecuteAsync("INSERT INTO scene_kb (scene_id, kb_entry_id) VALUES (@sceneId, @entryId)", new { sceneId = _sceneOne, entryId = _entryId });
        await db.ExecuteAsync(
            "INSERT INTO ai_generation (book_id, scene_id, kind, model, request, status) VALUES (@bookId, @sceneId, 'beat_prose', 'm', '{}', 'pending')",
            new { bookId = _bookId, sceneId = _sceneOne });
    }

    [Test]
    [Order(1)]
    public async Task Get_ShouldReturnActsChaptersScenesInOrderWithContinuousNumbers()
    {
        var outline = await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web);

        outline!.BookId.ShouldBe(_bookId);
        outline.Acts.Select(a => a.Title).ShouldBe(["", "Part two"]);
        var chapters = outline.Acts.SelectMany(a => a.Chapters).ToList();
        chapters.Select(c => (c.Title, c.Number)).ShouldBe(
            [("Preface", (int?)null), ("First", 1), ("Second", 2), ("Cut chapter", null)]);
        chapters.Select(c => c.Archived).ShouldBe([false, false, false, true]);
        chapters.Single(c => c.Title == "First").ArchivedSceneCount.ShouldBe(1);
        chapters.Single(c => c.Title == "First").Slug.ShouldBe("first");
        chapters.SelectMany(c => c.Scenes).Select(s => (s.Title, s.Number)).ShouldBe(
            [("Epigraph", "1"), ("Opening", "1.1"), ("Middle", "1.2"), ("Twist", "2.1")]);
    }

    [Test]
    [Order(2)]
    public async Task Get_ShouldReturnSceneCardFields()
    {
        var outline = await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web);

        var scenes = outline!.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).ToDictionary(s => s.Title!);
        var opening = scenes["Opening"];
        opening.Id.ShouldBe(_sceneOne);
        opening.Summary.ShouldBe("The **hook**.");
        opening.Status.ShouldBe("outline");
        opening.HasPendingDraft.ShouldBeTrue();
        opening.Labels.ShouldBe([new LabelRefDto(_labelId, "Flashback", "category-amber")]);
        opening.LinkedEntries.ShouldBe([new EntryRefDto(_entryId, "Guyon")]);
        scenes["Middle"].Words.ShouldBe(5);
        scenes["Middle"].Status.ShouldBe("draft");
        scenes["Middle"].HasPendingDraft.ShouldBeFalse();
        scenes["Twist"].Words.ShouldBe(0);
    }

    [Test]
    [Order(3)]
    public async Task Get_WhenUnknownBook_404()
    {
        var response = await Client.GetAsync($"/api/books/{Guid.NewGuid()}/outline");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ApiError>(JsonSerializerOptions.Web))!.Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
