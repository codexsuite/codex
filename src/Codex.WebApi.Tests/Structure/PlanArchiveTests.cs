using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class PlanArchiveTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _chapterId;
    private Guid _sceneA;
    private Guid _sceneB;
    private Guid _useId;
    private Guid _sourceId;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        _chapterId = await TestRows.ChapterAsync(db, _bookId, await TestRows.FirstActAsync(db, _bookId), "a0", "One");
        _sceneA = await TestRows.SceneAsync(db, _bookId, _chapterId, "a0", "A");
        _sceneB = await TestRows.SceneAsync(db, _bookId, _chapterId, "a1", "B");
        _sourceId = await TestRows.SourceAsync(db, _bookId, "The Velocity Trap");
        _useId = await TestRows.UseAsync(db, _bookId, _sourceId, _sceneA, "a quoted line");
        await TestRows.ProseAsync(db, _sceneA, $"Before [a quoted line](ref:{_useId}) after.");
    }

    [Test]
    [Order(1)]
    public async Task Archive_ShouldTakeSceneOffTheBoardAndNameTheUses()
    {
        var response = await Client.PostAsync($"/api/scenes/{_sceneA}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ArchiveSceneResult>(JsonSerializerOptions.Web))!;
        result.SceneId.ShouldBe(_sceneA);
        result.ArchivedUses.ShouldBe([new SceneUseDto(_useId, _sourceId, "The Velocity Trap")]);
        var chapter = (await OutlineAsync()).Acts[0].Chapters.Single();
        chapter.Scenes.Select(s => (s.Id, s.Number)).ToList().ShouldBe([(_sceneB, "1.1")]);
        chapter.ArchivedSceneCount.ShouldBe(1);
    }

    [Test]
    [Order(2)]
    public async Task Archive_WhenAlreadyArchived_409()
    {
        var response = await Client.PostAsync($"/api/scenes/{_sceneA}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(3)]
    public async Task ArchivedScenes_ShouldListTheSceneWithItsUses()
    {
        var list = await Client.GetFromJsonAsync<List<ArchivedSceneDto>>($"/api/chapters/{_chapterId}/archived-scenes", JsonSerializerOptions.Web);

        var scene = list.ShouldHaveSingleItem();
        (scene.Id, scene.ChapterNumber, scene.WasSceneNumber, scene.Title, scene.UseCount).ShouldBe((_sceneA, 1, 1, "A", 1));
    }

    [Test]
    [Order(4)]
    public async Task Restore_ShouldReturnSceneToItsPlaceAndUses()
    {
        var response = await Client.PostAsync($"/api/scenes/{_sceneA}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<RestoreSceneResult>(JsonSerializerOptions.Web))!;
        result.RestoredUses.Select(u => u.UseId).ShouldBe([_useId]);
        var chapter = (await OutlineAsync()).Acts[0].Chapters.Single();
        chapter.Scenes.Select(s => (s.Id, s.Number)).ToList().ShouldBe([(_sceneA, "1.1"), (_sceneB, "1.2")]);
        chapter.ArchivedSceneCount.ShouldBe(0);
    }

    [Test]
    [Order(5)]
    public async Task Restore_WhenSceneIsLive_409()
    {
        var response = await Client.PostAsync($"/api/scenes/{_sceneA}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(6)]
    public async Task Restore_WhenAnotherSceneTookItsPosition_ShouldPickAFreeKey()
    {
        await using var db = await OpenDatabaseAsync();
        await Client.PostAsync($"/api/scenes/{_sceneA}/archive", null);
        var taker = await TestRows.SceneAsync(db, _bookId, _chapterId, "a0", "Taker");

        var response = await Client.PostAsync($"/api/scenes/{_sceneA}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapter = (await OutlineAsync()).Acts[0].Chapters.Single();
        chapter.Scenes.Select(s => s.Id).ShouldBe([taker, _sceneA, _sceneB]);
    }

    [Test]
    [Order(7)]
    public async Task Archive_WhenSceneMissing_404()
    {
        var response = await Client.PostAsync($"/api/scenes/{Guid.NewGuid()}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(8)]
    public async Task ArchivedScenes_WhenChapterMissing_404()
    {
        var response = await Client.GetAsync($"/api/chapters/{Guid.NewGuid()}/archived-scenes");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private async Task<OutlineDto> OutlineAsync()
        => (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web))!;
}
