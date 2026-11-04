using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

[TestFixture]
public class SceneKbTests : BookContentFixture
{
    private Guid _scene;
    private Guid _entry;
    private Guid _seriesEntry;
    private Guid _foreignEntry;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        var series = await AddSeriesAsync("Cycle");
        var book = await AddBookAsync("Tides", series);
        var other = await AddBookAsync("Elsewhere");
        _entry = await AddEntryAsync(book, await AddCategoryAsync(book, "People"), "Anna");
        _seriesEntry = await AddEntryAsync(series, await AddCategoryAsync(series, "Places"), "Harbour");
        _foreignEntry = await AddEntryAsync(other, await AddCategoryAsync(other, "People"), "Stranger");
        _scene = await AddSceneAsync(book, await AddChapterAsync(book));
    }

    [Test, Order(1)]
    public async Task Post_ShouldLink_Idempotent()
    {
        for (var i = 0; i < 2; i++)
        {
            (await Client.PostAsJsonAsync($"/api/scenes/{_scene}/kb", new LinkKbEntryRequest(_entry)))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        (await Client.PostAsJsonAsync($"/api/scenes/{_scene}/kb", new LinkKbEntryRequest(_seriesEntry)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SceneLinksAsync(_scene)).ShouldBe(new[] { _entry, _seriesEntry }.Order().ToArray());
    }

    [Test, Order(2)]
    public async Task Delete_ShouldUnlink()
    {
        (await Client.DeleteAsync($"/api/scenes/{_scene}/kb/{_entry}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"/api/scenes/{_scene}/kb/{_entry}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SceneLinksAsync(_scene)).ShouldBe([_seriesEntry]);
        (await ArchivedAtAsync(_entry)).ShouldNotBeNull();
    }

    [Test, Order(3)]
    public async Task Post_WhenEntryFromOtherBook_ShouldReject()
    {
        var error = await ApiErrors.ReadApiErrorAsync(
            await Client.PostAsJsonAsync($"/api/scenes/{_scene}/kb", new LinkKbEntryRequest(_foreignEntry)),
            HttpStatusCode.BadRequest);

        error.Code.ShouldBe("validation");
        (await SceneLinksAsync(_scene)).ShouldBe([_seriesEntry]);
    }

    [Test, Order(4)]
    public async Task Post_WhenUnknownEntryOrScene_ShouldReturnNotFound()
    {
        (await ApiErrors.ReadApiErrorAsync(
            await Client.PostAsJsonAsync($"/api/scenes/{_scene}/kb", new LinkKbEntryRequest(Guid.NewGuid())),
            HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        (await ApiErrors.ReadApiErrorAsync(
            await Client.PostAsJsonAsync($"/api/scenes/{Guid.NewGuid()}/kb", new LinkKbEntryRequest(_entry)),
            HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(5)]
    public async Task Post_WhenLinkedAgainAfterUnlink_ShouldReviveTheSameRow()
    {
        (await Client.PostAsJsonAsync($"/api/scenes/{_scene}/kb", new LinkKbEntryRequest(_entry)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SceneLinksAsync(_scene)).ShouldBe(new[] { _entry, _seriesEntry }.Order().ToArray());
        (await ArchivedAtAsync(_entry)).ShouldBeNull();
        await using var connection = await OpenDatabaseAsync();
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene_kb WHERE scene_id = @_scene", new { _scene })).ShouldBe(2);
    }

    private async Task<DateTimeOffset?> ArchivedAtAsync(Guid entryId)
    {
        await using var connection = await OpenDatabaseAsync();
        return await connection.QuerySingleAsync<DateTimeOffset?>(
            "SELECT archived_at FROM scene_kb WHERE scene_id = @_scene AND kb_entry_id = @entryId", new { _scene, entryId });
    }
}
