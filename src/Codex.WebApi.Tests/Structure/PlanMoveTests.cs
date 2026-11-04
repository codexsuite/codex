using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class PlanMoveTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _actOne;
    private Guid _actTwo;
    private Guid _chapterOne;
    private Guid _chapterTwo;
    private Guid _sceneA;
    private Guid _sceneB;
    private Guid _sceneC;
    private Guid _otherBookChapter;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        _actOne = await TestRows.FirstActAsync(db, _bookId);
        _actTwo = await TestRows.ActAsync(db, _bookId, "a1", "Two");
        _chapterOne = await TestRows.ChapterAsync(db, _bookId, _actOne, "a0", "One");
        _chapterTwo = await TestRows.ChapterAsync(db, _bookId, _actOne, "a1", "Two");
        _sceneA = await TestRows.SceneAsync(db, _bookId, _chapterOne, "a0", "A");
        _sceneB = await TestRows.SceneAsync(db, _bookId, _chapterOne, "a1", "B");
        _sceneC = await TestRows.SceneAsync(db, _bookId, _chapterOne, "a2", "C");
        var other = await TestRows.BookAsync(db, "Other");
        _otherBookChapter = await TestRows.ChapterAsync(db, other, await TestRows.FirstActAsync(db, other), "a0", "Elsewhere");
    }

    [Test]
    [Order(1)]
    public async Task MoveScene_WhenToFirst_ShouldReorderAndReturnOutline()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneC}/position", new MoveSceneRequest(_chapterOne, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var outline = (await response.Content.ReadFromJsonAsync<OutlineDto>(JsonSerializerOptions.Web))!;
        var chapter = outline.Acts[0].Chapters.Single(c => c.Id == _chapterOne);
        chapter.Scenes.Select(s => s.Id).ShouldBe([_sceneC, _sceneA, _sceneB]);
        chapter.Scenes.Select(s => s.Number).ShouldBe(["1.1", "1.2", "1.3"]);
    }

    [Test]
    [Order(2)]
    public async Task MoveScene_WhenAfterASibling_ShouldPlaceItRightAfter()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneC}/position", new MoveSceneRequest(_chapterOne, _sceneA));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ChapterSceneIdsAsync(_chapterOne)).ShouldBe([_sceneA, _sceneC, _sceneB]);
    }

    [Test]
    [Order(3)]
    public async Task MoveScene_WhenToAnotherChapter_ShouldRenumberBoth()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneB}/position", new MoveSceneRequest(_chapterTwo, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ChapterSceneIdsAsync(_chapterOne)).ShouldBe([_sceneA, _sceneC]);
        (await ChapterSceneIdsAsync(_chapterTwo)).ShouldBe([_sceneB]);
    }

    [Test]
    [Order(4)]
    public async Task MoveChapter_WhenToAnotherAct_ShouldRenumberChapters()
    {
        var response = await Client.PutAsJsonAsync($"/api/chapters/{_chapterOne}/position", new MoveChapterRequest(_actTwo, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var outline = (await response.Content.ReadFromJsonAsync<OutlineDto>(JsonSerializerOptions.Web))!;
        outline.Acts.Select(a => a.Chapters.Select(c => (c.Id, c.Number)).ToList()).ToList()
            .ShouldBe([[(_chapterTwo, 1)], [(_chapterOne, 2)]]);
    }

    [Test]
    [Order(5)]
    public async Task MoveAct_WhenToFirst_ShouldReorderActs()
    {
        var response = await Client.PutAsJsonAsync($"/api/acts/{_actTwo}/position", new MoveActRequest(null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var outline = (await response.Content.ReadFromJsonAsync<OutlineDto>(JsonSerializerOptions.Web))!;
        outline.Acts.Select(a => a.Id).ShouldBe([_actTwo, _actOne]);
        outline.Acts.SelectMany(a => a.Chapters).Select(c => c.Id).ShouldBe([_chapterOne, _chapterTwo]);
    }

    [Test]
    [Order(6)]
    public async Task MoveScene_WhenChapterInAnotherBook_400AndNothingMoves()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneA}/position", new MoveSceneRequest(_otherBookChapter, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await ChapterSceneIdsAsync(_chapterOne)).ShouldContain(_sceneA);
    }

    [Test]
    [Order(7)]
    public async Task MoveChapter_WhenActInAnotherBook_400()
    {
        await using var db = await OpenDatabaseAsync();
        var foreignAct = await db.ExecuteScalarAsync<Guid>("SELECT act_id FROM chapter WHERE id = @id", new { id = _otherBookChapter });

        var response = await Client.PutAsJsonAsync($"/api/chapters/{_chapterOne}/position", new MoveChapterRequest(foreignAct, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(8)]
    public async Task MoveScene_WhenAfterIdIsNotInTheTarget_400()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneA}/position", new MoveSceneRequest(_chapterOne, _sceneB));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(9)]
    public async Task MoveScene_WhenSceneMissing_404()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{Guid.NewGuid()}/position", new MoveSceneRequest(_chapterOne, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(10)]
    public async Task MoveScene_WhenTargetChapterMissing_404()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneA}/position", new MoveSceneRequest(Guid.NewGuid(), null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private async Task<List<Guid>> ChapterSceneIdsAsync(Guid chapterId)
    {
        var outline = (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web))!;
        return outline.Acts.SelectMany(a => a.Chapters).Single(c => c.Id == chapterId).Scenes.Select(s => s.Id).ToList();
    }
}
