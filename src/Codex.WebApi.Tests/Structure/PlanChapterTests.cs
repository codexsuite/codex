using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

// Rename, delete (archive) and restore one chapter: the slug and the prose never change (cross-cutting rule R1).
[TestFixture]
public class PlanChapterTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _actId;
    private Guid _chapterId;
    private Guid _otherChapterId;
    private Guid _sceneId;
    private Guid _useId;
    private string _slug = "";

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        _actId = await TestRows.FirstActAsync(db, _bookId);
        _chapterId = await TestRows.ChapterAsync(db, _bookId, _actId, "a0", "Shipping Small");
        _otherChapterId = await TestRows.ChapterAsync(db, _bookId, _actId, "a1", "Next");
        _sceneId = await TestRows.SceneAsync(db, _bookId, _chapterId, "a0");
        var source = await TestRows.SourceAsync(db, _bookId, "The Velocity Trap");
        _useId = await TestRows.UseAsync(db, _bookId, source, _sceneId, "a quoted line");
        await TestRows.ProseAsync(db, _sceneId, $"Before [a quoted line](ref:{_useId}) after.\n");
        _slug = await db.ExecuteScalarAsync<string>("SELECT slug FROM chapter WHERE id = @_chapterId", new { _chapterId }) ?? "";
    }

    [Test, Order(1)]
    public async Task Rename_ShouldChangeTheTitleAndKeepTheSlug()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_chapterId}", new PatchChapterRequest("  Shipping Small, Safely "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapter = (await response.Content.ReadFromJsonAsync<OutlineChapterDto>(JsonSerializerOptions.Web))!;
        (chapter.Title, chapter.Slug, chapter.Number).ShouldBe(("Shipping Small, Safely", _slug, 1));
    }

    [Test, Order(2)]
    public async Task Rename_WhenTitleIsEmpty_400AndNothingChanges()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_chapterId}", new PatchChapterRequest("   "));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await ChapterAsync(_chapterId)).Title.ShouldBe("Shipping Small, Safely");
    }

    [Test, Order(3)]
    public async Task Rename_WhenAnotherChapterHasTheTitle_ShouldAllowItAndKeepSlugsApart()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_otherChapterId}", new PatchChapterRequest("Shipping Small, Safely"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ChapterAsync(_chapterId)).Slug.ShouldBe(_slug);
        (await ChapterAsync(_otherChapterId)).Slug.ShouldNotBe(_slug);
    }

    [Test, Order(4)]
    public async Task Archive_ShouldTakeTheChapterOffTheNumberingAndNameTheScenesAndUses()
    {
        var response = await Client.PostAsync($"/api/chapters/{_chapterId}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ArchiveChapterResult>(JsonSerializerOptions.Web))!;
        (result.ChapterId, result.SceneCount).ShouldBe((_chapterId, 1));
        result.ArchivedUses.Select(u => u.UseId).ShouldBe([_useId]);
        var archived = await ChapterAsync(_chapterId);
        (archived.Archived, archived.Number, archived.Title).ShouldBe((true, null, "Shipping Small, Safely"));
        (await ChapterAsync(_otherChapterId)).Number.ShouldBe(1);
    }

    [Test, Order(5)]
    public async Task Archive_WhenAlreadyArchived_409()
    {
        var response = await Client.PostAsync($"/api/chapters/{_chapterId}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(6)]
    // Main's PATCH /api/chapters/{id} (#69) edits live chapters only: an archived one is not_found and keeps its title.
    public async Task Rename_WhenArchived_ShouldBeNotFoundAndKeepTheTitle()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_chapterId}", new PatchChapterRequest("Kept"));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        (await ChapterAsync(_chapterId)).Title.ShouldBe("Shipping Small, Safely");
    }

    [Test, Order(7)]
    public async Task Restore_ShouldBringBackNumberScenesAndTheProseUntouched()
    {
        var response = await Client.PostAsync($"/api/chapters/{_chapterId}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapter = await ChapterAsync(_chapterId);
        (chapter.Archived, chapter.Number, chapter.Scenes.Count).ShouldBe((false, 1, 1));
        var prose = (await Client.GetFromJsonAsync<Codex.Contracts.Manuscript.ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web))!;
        prose.Body.ShouldBe($"Before [a quoted line](ref:{_useId}) after.\n");
    }

    [Test, Order(8)]
    public async Task Restore_WhenNotArchived_409()
    {
        var response = await Client.PostAsync($"/api/chapters/{_chapterId}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(9)]
    public async Task Rename_WhenChapterMissing_404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{Guid.NewGuid()}", new PatchChapterRequest("x"));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private async Task<OutlineChapterDto> ChapterAsync(Guid chapterId)
        => (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web))!
            .Acts.SelectMany(a => a.Chapters).Single(c => c.Id == chapterId);
}
