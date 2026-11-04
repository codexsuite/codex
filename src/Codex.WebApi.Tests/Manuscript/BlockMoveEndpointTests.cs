using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Manuscript;

[TestFixture]
public class BlockMoveEndpointTests : SchemaApiFixture
{
    private Guid _chapterId;
    private Guid _sceneA;
    private Guid _sceneB;
    private Guid _beat;
    private string _beatLine = "";

    private IProseStore Store => Host.Services.GetRequiredService<IProseStore>();

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        Guid bookId;
        (bookId, _chapterId, _sceneA) = await TestRows.SingleSceneAsync(db);
        _sceneB = await TestRows.SceneAsync(db, bookId, _chapterId, "b0");
        _beat = await TestRows.BeatAsync(db, _sceneA);
        _beatLine = $"::beat{{id=\"{_beat}\"}}";
        await TestRows.ProseAsync(db, _sceneA, $"One.\n\n{_beatLine}\n\nTwo.\n");
        await TestRows.ProseAsync(db, _sceneB, "Other scene.\n");
    }

    private async Task<(string Body, int Version)> ProseOfAsync(Guid sceneId)
    {
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose", JsonSerializerOptions.Web))!;
        return (prose.Body, prose.Version);
    }

    [Test]
    [Order(1)]
    public async Task UpdateMany_WhenEitherVersionIsStale_ShouldChangeNothing()
    {
        var thrown = await Should.ThrowAsync<ApiException>(() => Store.UpdateManyAsync(
            [(_sceneA, 1, _ => "A changed.\n"), (_sceneB, 9, _ => "B changed.\n")], "editor"));

        thrown.Code.ShouldBe(ApiErrorCodes.Conflict);
        (await ProseOfAsync(_sceneA)).ShouldBe(($"One.\n\n{_beatLine}\n\nTwo.\n", 1));
        (await ProseOfAsync(_sceneB)).ShouldBe(("Other scene.\n", 1));
    }

    [Test]
    [Order(2)]
    public async Task Move_WhenTheSecondWriteIsStale_ShouldLeaveBothScenesAndTheBeatAlone()
    {
        var response = await Client.PostAsJsonAsync("/api/blocks/move",
            new MoveBlockRequest(_sceneA, 1, "One.\n\nTwo.\n", _sceneB, 5, $"Other scene.\n\n{_beatLine}\n"));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        (await ProseOfAsync(_sceneA)).ShouldBe(($"One.\n\n{_beatLine}\n\nTwo.\n", 1));
        (await ProseOfAsync(_sceneB)).ShouldBe(("Other scene.\n", 1));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM beat WHERE id = @_beat", new { _beat })).ShouldBe(_sceneA);
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM beat WHERE id = @_beat", new { _beat })).ShouldBeTrue();
    }

    [Test]
    [Order(3)]
    public async Task Move_WhenTheSameScene_ShouldReturn400()
    {
        var response = await Client.PostAsJsonAsync("/api/blocks/move", new MoveBlockRequest(_sceneA, 1, "x\n", _sceneA, 1, "x\n"));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(4)]
    public async Task Move_WhenABeatLeavesWithItsBlock_ShouldMoveTheBeatRowAndBothBodies()
    {
        var response = await Client.PostAsJsonAsync("/api/blocks/move",
            new MoveBlockRequest(_sceneA, 1, "One.\n\nTwo.\n", _sceneB, 1, $"Other scene.\n\n{_beatLine}\n"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<MoveBlockResult>(JsonSerializerOptions.Web))!.ShouldBe(new MoveBlockResult(2, 2));
        (await ProseOfAsync(_sceneA)).ShouldBe(("One.\n\nTwo.\n", 2));
        (await ProseOfAsync(_sceneB)).ShouldBe(($"Other scene.\n\n{_beatLine}\n", 2));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM beat WHERE id = @_beat", new { _beat })).ShouldBe(_sceneB);
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM beat WHERE id = @_beat", new { _beat })).ShouldBeTrue();
    }

    [Test]
    [Order(5)]
    public async Task Split_WhenVersionIsStale_ShouldReturn409AndAddNoScene()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneB}/split", new SplitSceneRequest("Other scene.\n", $"{_beatLine}\n", 9));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene WHERE chapter_id = @_chapterId", new { _chapterId })).ShouldBe(2);
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM beat WHERE id = @_beat", new { _beat })).ShouldBe(_sceneB);
    }

    [Test]
    [Order(6)]
    public async Task Split_ShouldCreateTheNextSceneWithTheTailAndItsBeats()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneB}/split", new SplitSceneRequest("Other scene.\n", $"{_beatLine}\n\nTail text.\n", 2));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<SplitSceneResult>(JsonSerializerOptions.Web))!;
        (result.Version, result.NewSceneVersion).ShouldBe((3, 1));
        (await ProseOfAsync(_sceneB)).ShouldBe(("Other scene.\n", 3));
        (await ProseOfAsync(result.NewSceneId)).ShouldBe(($"{_beatLine}\n\nTail text.\n", 1));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM beat WHERE id = @_beat", new { _beat })).ShouldBe(result.NewSceneId);
        var order = await db.QueryAsync<Guid>("SELECT id FROM scene WHERE chapter_id = @_chapterId AND archived_at IS NULL ORDER BY position", new { _chapterId });
        order.ToList().ShouldBe([_sceneA, _sceneB, result.NewSceneId]);
    }

    [Test]
    [Order(7)]
    public async Task Move_WhenABodyDropsABeatLineWithoutPlacingItElsewhere_ShouldReturn400AndChangeNothing()
    {
        await using var db = await OpenDatabaseAsync();
        var holder = await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM beat WHERE id = @_beat", new { _beat });
        var (holderBody, holderVersion) = await ProseOfAsync(holder);
        var (bodyA, versionA) = await ProseOfAsync(_sceneA);

        var response = await Client.PostAsJsonAsync("/api/blocks/move", new MoveBlockRequest(holder, holderVersion, "Tail text.\n", _sceneA, versionA, bodyA));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await ProseOfAsync(holder)).ShouldBe((holderBody, holderVersion));
        (await ProseOfAsync(_sceneA)).ShouldBe((bodyA, versionA));
    }

    [Test]
    [Order(8)]
    public async Task Move_WhenTheSecondScenesTriggerRejectsItsBody_ShouldRollBackTheFirstScene()
    {
        await using var db = await OpenDatabaseAsync();
        var bookId = await db.ExecuteScalarAsync<Guid>("SELECT book_id FROM scene WHERE id = @_sceneA", new { _sceneA });
        var other = await TestRows.SceneAsync(db, bookId, _chapterId, "c0");
        var foreignBeat = await TestRows.BeatAsync(db, other);
        var (bodyA, versionA) = await ProseOfAsync(_sceneA);
        var (bodyB, versionB) = await ProseOfAsync(_sceneB);

        // The edits are listed A then B; B carries a beat line for a beat of a third scene, which the schema rejects.
        var response = await Client.PostAsJsonAsync("/api/blocks/move",
            new MoveBlockRequest(_sceneA, versionA, "A rewritten.\n", _sceneB, versionB, $"{bodyB}\n::beat{{id=\"{foreignBeat}\"}}\n"));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await ProseOfAsync(_sceneA)).ShouldBe((bodyA, versionA));
        (await ProseOfAsync(_sceneB)).ShouldBe((bodyB, versionB));
    }
}
