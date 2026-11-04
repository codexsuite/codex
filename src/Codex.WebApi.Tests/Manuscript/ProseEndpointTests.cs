using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Structure;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Manuscript;

[TestFixture]
public class ProseEndpointTests : ApiFixture
{
    private Guid _bookId;
    private Guid _sceneId;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, _sceneId, "First draft.");
    }

    [Test]
    [Order(1)]
    public async Task Get_ShouldReturnBodyAndVersion()
    {
        var prose = await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web);

        prose!.SceneId.ShouldBe(_sceneId);
        prose.Body.ShouldBe("First draft.");
        prose.Version.ShouldBe(1);
    }

    [Test]
    [Order(2)]
    public async Task Put_WhenVersionCurrent_ShouldSaveAndBumpVersion()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/prose", new SaveProseRequest("Second draft.", 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SaveProseResult>(JsonSerializerOptions.Web))!.Version.ShouldBe(2);
        var prose = await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web);
        (prose!.Body, prose.Version).ShouldBe(("Second draft.", 2));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT updated_by FROM prose WHERE scene_id = @_sceneId", new { _sceneId })).ShouldBe(SeedIds.Users.Jd);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @_sceneId AND source = 'autosave'", new { _sceneId })).ShouldBe(1);
    }

    [Test]
    [Order(3)]
    public async Task Put_WhenStale_ShouldSaveLastWriteAndKeepTheReplacedText()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/prose", new SaveProseRequest("Last write.", 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SaveProseResult>(JsonSerializerOptions.Web))!.Version.ShouldBe(3);
        var prose = await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web);
        (prose!.Body, prose.Version).ShouldBe(("Last write.", 3));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM prose_revision WHERE scene_id = @_sceneId AND source = 'replaced' AND body = 'Second draft.'", new { _sceneId })).ShouldBe(1);
    }

    [Test]
    [Order(9)]
    public async Task Put_WhenStaleAndSameText_ShouldKeepNothing()
    {
        await using var db = await OpenDatabaseAsync();
        var chapterId = await db.ExecuteScalarAsync<Guid>("SELECT chapter_id FROM scene WHERE id = @_sceneId", new { _sceneId });
        var scene = await TestRows.SceneAsync(db, _bookId, chapterId, "a3");
        await TestRows.ProseAsync(db, scene, "Same.");
        await db.ExecuteAsync("UPDATE prose SET body = 'Other tab.', version = 2 WHERE scene_id = @scene", new { scene });

        var response = await Client.PutAsJsonAsync($"/api/scenes/{scene}/prose", new SaveProseRequest("Other tab.", 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @scene AND source = 'replaced'", new { scene })).ShouldBe(0);
    }

    [Test]
    [Order(4)]
    [CancelAfter(10000)]
    public async Task Put_WhenSaved_ShouldPublishProseSaved()
    {
        var published = new TaskCompletionSource<BookEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = Host.Services.GetRequiredService<IBookEvents>().Subscribe(_bookId, e =>
        {
            published.TrySetResult(e);
            return Task.CompletedTask;
        });

        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/prose", new SaveProseRequest("Third draft.", 3));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await published.Task).ShouldBe(new ProseSaved(_bookId, _sceneId, 4, "editor"));
    }

    [Test]
    [Order(5)]
    public async Task Put_WhenBodyMissing_400()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/prose", new { version = 4 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>(JsonSerializerOptions.Web))!.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(6)]
    public async Task Get_WhenUnknownScene_404()
    {
        var response = await Client.GetAsync($"/api/scenes/{Guid.NewGuid()}/prose");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ApiError>(JsonSerializerOptions.Web))!.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(7)]
    public async Task Put_WhenUnknownScene_404()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{Guid.NewGuid()}/prose", new SaveProseRequest("x", 0));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    [Order(8)]
    public async Task Put_WhenSceneHasNoProseYet_ShouldCreateAtVersion1()
    {
        await using var db = await OpenDatabaseAsync();
        var chapterId = await db.ExecuteScalarAsync<Guid>("SELECT chapter_id FROM scene WHERE id = @_sceneId", new { _sceneId });
        var emptyScene = await TestRows.SceneAsync(db, _bookId, chapterId, "a1");

        var before = await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{emptyScene}/prose", JsonSerializerOptions.Web);
        var response = await Client.PutAsJsonAsync($"/api/scenes/{emptyScene}/prose", new SaveProseRequest("Hello.", 0));

        (before!.Body, before.Version).ShouldBe(("", 0));
        (await response.Content.ReadFromJsonAsync<SaveProseResult>(JsonSerializerOptions.Web))!.Version.ShouldBe(1);
    }
}
