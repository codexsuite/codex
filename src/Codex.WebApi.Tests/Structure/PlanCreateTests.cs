using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class PlanCreateTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _act;
    private Guid _chapterOne;
    private Guid _chapterTwo;
    private Guid _archivedChapter;
    private Guid _sceneA;
    private Guid _sceneB;
    private Guid _sceneInTwo;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        _act = await TestRows.FirstActAsync(db, _bookId);
        _chapterOne = await TestRows.ChapterAsync(db, _bookId, _act, "a0", "One");
        _chapterTwo = await TestRows.ChapterAsync(db, _bookId, _act, "a1", "Two");
        _archivedChapter = await TestRows.ChapterAsync(db, _bookId, _act, "a2", "Gone");
        await db.ExecuteAsync("UPDATE chapter SET archived_at = now() WHERE id = @id", new { id = _archivedChapter });
        _sceneA = await TestRows.SceneAsync(db, _bookId, _chapterOne, "a0", "A");
        _sceneB = await TestRows.SceneAsync(db, _bookId, _chapterOne, "a1", "B");
        _sceneInTwo = await TestRows.SceneAsync(db, _bookId, _chapterTwo, "a0", "Elsewhere");
    }

    [Test]
    [Order(1)]
    public async Task CreateChapter_WhenNoAfterId_ShouldAppendWithSlugAndNumber()
    {
        var response = await Client.PostAsJsonAsync($"/api/acts/{_act}/chapters", new CreateChapterRequest("The Fall", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var chapter = (await response.Content.ReadFromJsonAsync<OutlineChapterDto>(JsonSerializerOptions.Web))!;
        chapter.Title.ShouldBe("The Fall");
        chapter.Slug.ShouldBe("the-fall");
        chapter.Number.ShouldBe(3);
        chapter.Scenes.ShouldBeEmpty();
    }

    [Test]
    [Order(2)]
    public async Task CreateChapter_WhenAfterIdAndNoTitle_ShouldInsertAfterItWithDefaultTitle()
    {
        var response = await Client.PostAsJsonAsync($"/api/acts/{_act}/chapters", new CreateChapterRequest(null, _chapterOne));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var chapter = (await response.Content.ReadFromJsonAsync<OutlineChapterDto>(JsonSerializerOptions.Web))!;
        chapter.Title.ShouldBe("New chapter");
        chapter.Number.ShouldBe(2);
    }

    [Test]
    [Order(3)]
    public async Task CreateScene_WhenAfterFirst_ShouldRenumberFollowingScenes()
    {
        var response = await Client.PostAsJsonAsync($"/api/chapters/{_chapterOne}/scenes", new CreateSceneRequest("Middle", _sceneA));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var scene = (await response.Content.ReadFromJsonAsync<OutlineSceneDto>(JsonSerializerOptions.Web))!;
        scene.Title.ShouldBe("Middle");
        scene.Status.ShouldBe("outline");
        scene.Number.ShouldBe("1.2");
        await using var db = await OpenDatabaseAsync();
        var order = (await db.QueryAsync<Guid>(
            "SELECT id FROM scene WHERE chapter_id = @id ORDER BY position COLLATE \"C\"", new { id = _chapterOne })).ToList();
        order.ShouldBe([_sceneA, scene.Id, _sceneB]);
    }

    [Test]
    [Order(4)]
    public async Task CreateScene_WhenNoAfterId_ShouldAppend()
    {
        var response = await Client.PostAsJsonAsync($"/api/chapters/{_chapterOne}/scenes", new CreateSceneRequest(null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var scene = (await response.Content.ReadFromJsonAsync<OutlineSceneDto>(JsonSerializerOptions.Web))!;
        scene.Title.ShouldBeNull();
        scene.Number.ShouldBe("1.4");
    }

    [Test]
    [Order(5)]
    public async Task CreateScene_WhenAfterIdIsInAnotherChapter_400()
    {
        var response = await Client.PostAsJsonAsync($"/api/chapters/{_chapterOne}/scenes", new CreateSceneRequest(null, _sceneInTwo));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(6)]
    public async Task CreateChapter_WhenAfterIdIsNotInTheAct_400()
    {
        var response = await Client.PostAsJsonAsync($"/api/acts/{_act}/chapters", new CreateChapterRequest(null, _archivedChapter));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(7)]
    public async Task CreateScene_WhenChapterArchived_404()
    {
        var response = await Client.PostAsJsonAsync($"/api/chapters/{_archivedChapter}/scenes", new CreateSceneRequest(null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(8)]
    public async Task CreateScene_WhenChapterUnknown_404()
    {
        var response = await Client.PostAsJsonAsync($"/api/chapters/{Guid.NewGuid()}/scenes", new CreateSceneRequest(null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(9)]
    public async Task CreateChapter_WhenActUnknown_404()
    {
        var response = await Client.PostAsJsonAsync($"/api/acts/{Guid.NewGuid()}/chapters", new CreateChapterRequest(null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(10)]
    public async Task CreateChapter_WhenTitleTooLong_400()
    {
        var response = await Client.PostAsJsonAsync($"/api/acts/{_act}/chapters", new CreateChapterRequest(new string('x', 201), null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(20)]
    public async Task CreateScene_WhenCreatedConcurrently_ShouldGiveEachItsOwnPosition()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Client.PostAsJsonAsync($"/api/chapters/{_chapterTwo}/scenes", new CreateSceneRequest(null, null))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        await using var db = await OpenDatabaseAsync();
        var positions = (await db.QueryAsync<string>(
            "SELECT position FROM scene WHERE chapter_id = @id AND archived_at IS NULL", new { id = _chapterTwo })).ToList();
        positions.Distinct().Count().ShouldBe(positions.Count);
    }
}
