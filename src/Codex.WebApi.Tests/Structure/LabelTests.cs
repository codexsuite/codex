using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class LabelTests : ApiFixture
{
    private Guid _seriesId;
    private Guid _bookId;
    private Guid _sceneId;
    private Guid _otherBookLabel;
    private Guid _seriesLabel;
    private Guid _labelA;
    private Guid _labelB;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _seriesId = await TestRows.SeriesAsync(db);
        _bookId = await TestRows.BookAsync(db, seriesId: _seriesId);
        var chapterId = await TestRows.ChapterAsync(db, _bookId, await TestRows.FirstActAsync(db, _bookId), "a0", "Chapter");
        _sceneId = await TestRows.SceneAsync(db, _bookId, chapterId, "a0");
        var otherBook = await TestRows.BookAsync(db, "Other");
        _otherBookLabel = await TestRows.LabelAsync(db, otherBook, "Elsewhere");
        _seriesLabel = await TestRows.LabelAsync(db, _seriesId, "Series-wide", "a5");
    }

    [Test]
    [Order(1)]
    public async Task CreateLabel_ShouldAppendAndAppearInBookAndSeriesScope()
    {
        _labelA = await CreateAsync("Flashback", "category-amber");
        _labelB = await CreateAsync("Foreshadowing", null);

        var labels = await Client.GetFromJsonAsync<List<LabelDto>>($"/api/books/{_bookId}/labels", JsonSerializerOptions.Web);
        labels!.Select(l => (l.Name, l.ScopeId, l.SceneCount)).ShouldBe(
            [("Flashback", _bookId, 0), ("Foreshadowing", _bookId, 0), ("Series-wide", _seriesId, 0)],
            ignoreOrder: true);
        labels!.Single(l => l.Id == _labelA).Color.ShouldBe("category-amber");
        labels!.Select(l => l.Id).ShouldNotContain(_otherBookLabel);
    }

    [Test]
    [Order(2)]
    public async Task CreateLabel_WhenDuplicateName_400or409()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_bookId}/labels", new CreateLabelRequest("Flashback", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadApiErrorAsync()).Code.ShouldBe("conflict");
    }

    [Test]
    [Order(3)]
    public async Task CreateLabel_WhenNameBlank_400()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_bookId}/labels", new CreateLabelRequest("   ", null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe("validation");
    }

    [Test]
    [Order(4)]
    public async Task CreateLabel_WhenUnknownBook_404()
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/labels", new CreateLabelRequest("x", null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe("not_found");
    }

    [Test]
    [Order(5)]
    public async Task PutLabels_ShouldReplaceSet()
    {
        (await SetLabelsAsync(_labelA, _labelB)).Select(l => l.Id).ShouldBe([_labelA, _labelB]);
        // Labels come back in position order ("a5" for the series label sorts before the appended "n…" keys).
        (await SetLabelsAsync(_labelB, _seriesLabel)).Select(l => l.Id).ShouldBe([_seriesLabel, _labelB]);

        (await SceneLabelIdsAsync()).ShouldBe([_labelB, _seriesLabel], ignoreOrder: true);
        var outline = await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web);
        outline!.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).Single().Labels.Select(l => l.Id)
            .ShouldBe([_seriesLabel, _labelB]);
        (await SetLabelsAsync()).ShouldBeEmpty();
        // Removed links are archived, so putting one back revives the same row instead of clashing with it.
        (await SetLabelsAsync(_labelB)).Select(l => l.Id).ShouldBe([_labelB]);
        (await SceneLabelIdsAsync()).ShouldBe([_labelB]);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM scene_label WHERE scene_id = @_sceneId AND label_id = @_labelB", new { _sceneId, _labelB })).ShouldBe(1);
        (await SetLabelsAsync()).ShouldBeEmpty();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM scene_label WHERE scene_id = @_sceneId AND archived_at IS NOT NULL", new { _sceneId })).ShouldBeGreaterThan(0);
    }

    [Test]
    [Order(6)]
    public async Task PutLabels_WhenLabelOutsideScope_400AndKeepsSet()
    {
        await SetLabelsAsync(_labelA);

        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/labels", new SetSceneLabelsRequest([_labelB, _otherBookLabel]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe("validation");
        (await SceneLabelIdsAsync()).ShouldBe([_labelA]);
    }

    [Test]
    [Order(7)]
    public async Task PutLabels_WhenUnknownScene_404()
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{Guid.NewGuid()}/labels", new SetSceneLabelsRequest([]));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe("not_found");
    }

    [Test]
    [Order(8)]
    public async Task PatchLabel_ShouldRenameRecolourAndRejectDuplicate()
    {
        var response = await Client.PatchAsJsonAsync($"/api/labels/{_labelB}", new PatchLabelRequest("Setup", "category-blue"));
        var renamed = await response.Content.ReadFromJsonAsync<LabelDto>(JsonSerializerOptions.Web);
        var clash = await Client.PatchAsJsonAsync($"/api/labels/{_labelB}", new PatchLabelRequest("Flashback", null));
        var cleared = await Client.PatchAsJsonAsync($"/api/labels/{_labelB}", new PatchLabelRequest(null, ""));

        (renamed!.Name, renamed.Color).ShouldBe(("Setup", "category-blue"));
        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await clash.ReadApiErrorAsync()).Code.ShouldBe("conflict");
        (await cleared.Content.ReadFromJsonAsync<LabelDto>(JsonSerializerOptions.Web))!.Color.ShouldBeNull();
    }

    [Test]
    [Order(9)]
    public async Task PatchLabel_WhenUnknown_404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/labels/{Guid.NewGuid()}", new PatchLabelRequest("x", null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe("not_found");
    }

    // Deleting a label that scenes carry archives it and takes it off those scenes (the scene_label rows stay, hidden
    // because their label is archived). LabelDto.SceneCount lets the client ask for confirmation first.
    [Test]
    [Order(10)]
    public async Task DeleteLabel_InUse_ShouldRemoveItFromScenes()
    {
        (await Labels()).Single(l => l.Id == _labelA).SceneCount.ShouldBe(1);

        var response = await Client.DeleteAsync($"/api/labels/{_labelA}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SceneLabelIdsAsync()).ShouldBeEmpty();
        (await Labels()).Select(l => l.Id).ShouldNotContain(_labelA);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM label WHERE id = @_labelA", new { _labelA })).ShouldBeTrue();
        (await Client.PatchAsJsonAsync($"/api/labels/{_labelA}", new PatchLabelRequest("Renamed", null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.DeleteAsync($"/api/labels/{_labelA}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/labels", new SetSceneLabelsRequest([_labelA]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    [Order(11)]
    public async Task CreateLabel_WhenTheNameBelongedToADeletedLabel_ShouldSucceed()
    {
        var again = await CreateAsync("Flashback", null);

        again.ShouldNotBe(_labelA);
        (await Labels()).Count(l => l.Name == "Flashback").ShouldBe(1);
    }

    [Test]
    [Order(12)]
    public async Task DeleteLabel_WhenUnknown_404()
    {
        var response = await Client.DeleteAsync($"/api/labels/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadApiErrorAsync()).Code.ShouldBe("not_found");
    }

    [Test]
    [Order(13)]
    public async Task PutLabels_WhenSceneArchived_ShouldReturn404()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE scene SET archived_at = now() WHERE id = @_sceneId", new { _sceneId });

        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/labels", new SetSceneLabelsRequest([_seriesLabel]));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    private async Task<Guid> CreateAsync(string name, string? color)
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{_bookId}/labels", new CreateLabelRequest(name, color));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LabelDto>(JsonSerializerOptions.Web))!.Id;
    }

    private async Task<List<LabelRefDto>> SetLabelsAsync(params Guid[] ids)
    {
        var response = await Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/labels", new SetSceneLabelsRequest(ids));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<LabelRefDto>>(JsonSerializerOptions.Web))!;
    }

    private async Task<List<LabelDto>> Labels()
        => (await Client.GetFromJsonAsync<List<LabelDto>>($"/api/books/{_bookId}/labels", JsonSerializerOptions.Web))!;

    private async Task<List<Guid>> SceneLabelIdsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return (await db.QueryAsync<Guid>("SELECT sl.label_id FROM scene_label sl JOIN label l ON l.id = sl.label_id WHERE sl.scene_id = @_sceneId AND sl.archived_at IS NULL AND l.archived_at IS NULL", new { _sceneId })).ToList();
    }
}
