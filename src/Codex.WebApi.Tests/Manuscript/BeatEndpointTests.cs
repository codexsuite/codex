using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

[TestFixture]
public class BeatEndpointTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _sceneId;
    private readonly Guid _beatId = Guid.NewGuid();

    private string BeatLine => $"::beat{{id=\"{_beatId}\"}}";

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, _sceneId, "First half.\n\nSecond half.\n");
    }

    [Test]
    [Order(1)]
    public async Task Insert_WhenVersionStale_ShouldReturn409AndLeaveNoBeat()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/beats", new InsertBeatRequest(_beatId, $"First half.\n\n{BeatLine}\n\nSecond half.\n", 7));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM beat WHERE id = @_beatId", new { _beatId })).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task Insert_WhenBodyHasNoBeatLine_ShouldReturn400()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/beats", new InsertBeatRequest(_beatId, "First half.\n", 1));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(3)]
    public async Task Insert_ShouldCreateTheBeatAndSaveTheBodyWithItsLine()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/beats", new InsertBeatRequest(_beatId, $"First half.\n\n{BeatLine}\n\nSecond half.\n", 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<InsertBeatResult>(JsonSerializerOptions.Web))!.ShouldBe(new InsertBeatResult(_beatId, 2));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM beat WHERE id = @_beatId", new { _beatId })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<int>("SELECT beat_number::int FROM beat_outline WHERE beat_id = @_beatId", new { _beatId })).ShouldBe(1);
    }

    [Test]
    [Order(4)]
    public async Task Patch_ShouldChangeOnlyTheFieldsSent()
    {
        var response = await Client.PatchAsJsonAsync($"/api/beats/{_beatId}", new PatchBeatRequest("Anna finds the map", null, 600));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var beat = (await response.Content.ReadFromJsonAsync<BeatDto>(JsonSerializerOptions.Web))!;
        (beat.Body, beat.TargetWordCount, beat.IsHidden).ShouldBe(("Anna finds the map", 600, false));
        var hidden = await Client.PatchAsJsonAsync($"/api/beats/{_beatId}", new PatchBeatRequest(null, true, null));
        var after = (await hidden.Content.ReadFromJsonAsync<BeatDto>(JsonSerializerOptions.Web))!;
        (after.Body, after.TargetWordCount, after.IsHidden).ShouldBe(("Anna finds the map", 600, true));
    }

    [Test]
    [Order(5)]
    public async Task Patch_WhenTargetNotPositive_ShouldReturn400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/beats/{_beatId}", new PatchBeatRequest(null, null, 0));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(6)]
    public async Task Patch_WhenBeatUnknown_ShouldReturn404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/beats/{Guid.NewGuid()}", new PatchBeatRequest("x", null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(7)]
    public async Task List_ShouldReturnTheBooksLiveBeatsWithTheirContext()
    {
        await using var db = await OpenDatabaseAsync();
        var category = await TestRows.CategoryAsync(db, _bookId, "People");
        var entry = await TestRows.EntryAsync(db, _bookId, category, "Anna");
        (await Client.PutAsJsonAsync($"/api/beats/{_beatId}/context", new SetBeatContextRequest([entry]))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var beats = (await Client.GetFromJsonAsync<BookBeatsDto>($"/api/books/{_bookId}/beats", JsonSerializerOptions.Web))!;

        var beat = beats.Beats.ShouldHaveSingleItem();
        (beat.Id, beat.SceneId, beat.Body, beat.TargetWordCount, beat.IsHidden).ShouldBe((_beatId, _sceneId, "Anna finds the map", 600, true));
        beat.ContextEntryIds.ShouldBe([entry]);
    }

    [Test]
    [Order(8)]
    public async Task Context_WhenEntryIsOutOfScope_ShouldReturn400()
    {
        await using var db = await OpenDatabaseAsync();
        var other = await TestRows.BookAsync(db, "Elsewhere");
        var foreign = await TestRows.EntryAsync(db, other, await TestRows.CategoryAsync(db, other, "People"), "Stranger");

        var response = await Client.PutAsJsonAsync($"/api/beats/{_beatId}/context", new SetBeatContextRequest([foreign]));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(9)]
    public async Task Insert_WhenTheBeatIdAlreadyExists_ShouldReturn409()
    {
        var current = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web))!;

        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/beats", new InsertBeatRequest(_beatId, current.Body, current.Version));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(10)]
    public async Task Insert_WhenTheBodyDropsAnExistingBeatLine_ShouldReturn400AndLeaveNoBeat()
    {
        var current = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web))!;
        var fresh = Guid.NewGuid();

        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/beats",
            new InsertBeatRequest(fresh, $"Only text.\n\n::beat{{id=\"{fresh}\"}}\n", current.Version));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM beat WHERE id = @fresh", new { fresh })).ShouldBe(0);
    }

    [Test]
    [Order(11)]
    public async Task Patch_WhenTitleSent_ShouldSetItTrimmedAndAnEmptyTitleShouldClearIt()
    {
        var titled = await Client.PatchAsJsonAsync($"/api/beats/{_beatId}", new PatchBeatRequest(null, null, null, "  Anna at the gate "));

        titled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var beat = (await titled.Content.ReadFromJsonAsync<BeatDto>(JsonSerializerOptions.Web))!;
        (beat.Title, beat.Body).ShouldBe(("Anna at the gate", "Anna finds the map"));
        var listed = (await Client.GetFromJsonAsync<BookBeatsDto>($"/api/books/{_bookId}/beats", JsonSerializerOptions.Web))!;
        listed.Beats.ShouldHaveSingleItem().Title.ShouldBe("Anna at the gate");

        var cleared = await Client.PatchAsJsonAsync($"/api/beats/{_beatId}", new PatchBeatRequest(null, null, null, ""));

        (await cleared.Content.ReadFromJsonAsync<BeatDto>(JsonSerializerOptions.Web))!.Title.ShouldBeEmpty();
    }
}
