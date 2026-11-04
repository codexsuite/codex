using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class ScenePatchTests : ApiFixture
{
    private Guid _bookId;
    private Guid _chapterId;
    private Guid _sceneId;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _chapterId, _sceneId) = await TestRows.SingleSceneAsync(db);
    }

    [Test]
    [Order(1)]
    public async Task Patch_WhenSummaryChanged_ShouldPersist()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest("Guyon **arrives**.", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var returned = await response.Content.ReadFromJsonAsync<OutlineSceneDto>(JsonSerializerOptions.Web);
        returned!.Summary.ShouldBe("Guyon **arrives**.");
        returned.Number.ShouldBe("1.1");
        (await OutlineSceneAsync()).Summary.ShouldBe("Guyon **arrives**.");
    }

    [Test]
    [Order(2)]
    public async Task Patch_WhenTitleAndStatusChanged_ShouldPersistAndKeepSummary()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest(null, "  Arrival  ", "draft"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var scene = await OutlineSceneAsync();
        (scene.Title, scene.Status, scene.Summary).ShouldBe(("Arrival", "draft", "Guyon **arrives**."));
    }

    [Test]
    [Order(3)]
    public async Task Patch_WhenSummaryEmpty_ShouldClearIt()
    {
        await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest("", null, null));

        (await OutlineSceneAsync()).Summary.ShouldBeNull();
    }

    [Test]
    [Order(4)]
    public async Task Patch_WhenStatusInvalid_400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest(null, null, "finished"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
        (await OutlineSceneAsync()).Status.ShouldBe("draft");
    }

    [Test]
    [Order(5)]
    public async Task Patch_WhenNothingGiven_400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest(null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(6)]
    public async Task Patch_WhenTitleTooLong_400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest(null, new string('x', 201), null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(7)]
    public async Task Patch_WhenUnknownScene_404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{Guid.NewGuid()}", new PatchSceneRequest("x", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(8)]
    public async Task Patch_WhenSceneArchived_ShouldReturn404AndChangeNothing()
    {
        await using var db = await OpenDatabaseAsync();
        var archived = await TestRows.SceneAsync(db, _bookId, _chapterId, "a1", summary: "Before", archived: true);

        var response = await Client.PatchAsJsonAsync($"/api/scenes/{archived}", new PatchSceneRequest("After", null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        (await db.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @archived", new { archived })).ShouldBe("Before");
    }

    [Test]
    [Order(9)]
    public async Task Patch_WhenActArchived_ShouldReturn404AndChangeNothing()
    {
        await using var db = await OpenDatabaseAsync();
        var act = await TestRows.ActAsync(db, _bookId, "b0", "Cut act");
        var chapter = await TestRows.ChapterAsync(db, _bookId, act, "a0", "Cut chapter");
        var scene = await TestRows.SceneAsync(db, _bookId, chapter, "a0", summary: "Before");
        await db.ExecuteAsync("UPDATE act SET archived_at = now() WHERE id = @act", new { act });

        var response = await Client.PatchAsJsonAsync($"/api/scenes/{scene}", new PatchSceneRequest("After", null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        (await db.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @scene", new { scene })).ShouldBe("Before");
    }

    private async Task<OutlineSceneDto> OutlineSceneAsync()
    {
        var outline = await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web);
        return outline!.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).Single(s => s.Id == _sceneId);
    }
}
