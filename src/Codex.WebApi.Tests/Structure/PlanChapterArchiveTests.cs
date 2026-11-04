using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class PlanChapterArchiveTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _actId;
    private Guid _first;
    private Guid _second;
    private Guid _useId;
    private Guid _sourceId;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        _actId = await TestRows.FirstActAsync(db, _bookId);
        _first = await TestRows.ChapterAsync(db, _bookId, _actId, "a0", "One");
        var scene = await TestRows.SceneAsync(db, _bookId, _first, "a0", "A");
        await TestRows.SceneAsync(db, _bookId, _first, "a1", "Cut earlier", archived: true);
        _second = await TestRows.ChapterAsync(db, _bookId, _actId, "a1", "Two");
        await TestRows.SceneAsync(db, _bookId, _second, "a0", "B");
        _sourceId = await TestRows.SourceAsync(db, _bookId, "The Velocity Trap");
        _useId = await TestRows.UseAsync(db, _bookId, _sourceId, scene, "a quoted line");
        await TestRows.ProseAsync(db, scene, $"Before [a quoted line](ref:{_useId}) after.");
    }

    [Test]
    [Order(1)]
    public async Task Archive_ShouldTakeTheChapterOutOfTheNumberingAndNameItsUses()
    {
        var response = await Client.PostAsync($"/api/chapters/{_first}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ArchiveChapterResult>(JsonSerializerOptions.Web))!;
        result.ChapterId.ShouldBe(_first);
        result.ArchivedUses.ShouldBe([new SceneUseDto(_useId, _sourceId, "The Velocity Trap")]);
        var chapters = (await OutlineAsync()).Acts.ShouldHaveSingleItem().Chapters;
        chapters.Single(c => c.Id == _first).ShouldSatisfyAllConditions(c => c.Archived.ShouldBeTrue(), c => c.Number.ShouldBeNull());
        chapters.Single(c => c.Id == _second).Number.ShouldBe(1);
    }

    [Test]
    [Order(2)]
    public async Task Archive_WhenAlreadyArchived_409()
    {
        var response = await Client.PostAsync($"/api/chapters/{_first}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(3)]
    public async Task Restore_WhenAnotherChapterTookItsPosition_ShouldPutItRightAfterThatOneWithOnlyItsLiveScenes()
    {
        await using var db = await OpenDatabaseAsync();
        var taker = await TestRows.ChapterAsync(db, _bookId, _actId, "a0", "Taker");

        var response = await Client.PostAsync($"/api/chapters/{_first}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapter = (await response.Content.ReadFromJsonAsync<OutlineChapterDto>(JsonSerializerOptions.Web))!;
        (chapter.Archived, chapter.Number).ShouldBe((false, 2));
        chapter.Scenes.ShouldHaveSingleItem().Title.ShouldBe("A");
        chapter.ArchivedSceneCount.ShouldBe(1);
        (await OutlineAsync()).Acts.ShouldHaveSingleItem().Chapters.Select(c => c.Id).ShouldBe([taker, _first, _second]);
    }

    [Test]
    [Order(4)]
    public async Task Restore_WhenChapterIsLive_409()
    {
        var response = await Client.PostAsync($"/api/chapters/{_first}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(5)]
    public async Task Restore_WhenItsActIsArchived_409()
    {
        await using var db = await OpenDatabaseAsync();
        var act = await TestRows.ActAsync(db, _bookId, "b0", "Doomed");
        var chapter = await TestRows.ChapterAsync(db, _bookId, act, "a0", "Inside");
        await db.ExecuteAsync("UPDATE chapter SET archived_at = now() WHERE id = @chapter", new { chapter });
        await db.ExecuteAsync("UPDATE act SET archived_at = now() WHERE id = @act", new { act });

        var response = await Client.PostAsync($"/api/chapters/{chapter}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(6)]
    public async Task Archive_WhenChapterUnknown_404()
    {
        var response = await Client.PostAsync($"/api/chapters/{Guid.NewGuid()}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private async Task<OutlineDto> OutlineAsync()
        => (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web))!;
}
