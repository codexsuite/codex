using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class ScenePlanNotesPatchTests : ApiFixture
{
    private Guid _sceneId;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_, _, _sceneId) = await TestRows.SingleSceneAsync(db);
    }

    [Test]
    [Order(1)]
    public async Task Patch_WhenOnlyPlanNotesGiven_ShouldPersistThemAndLeaveTheRestAlone()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest(null, null, null, "Purpose: set up the trap."));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await NotesAsync()).ShouldBe("Purpose: set up the trap.");
    }

    [Test]
    [Order(2)]
    public async Task Patch_WhenSummaryChangedWithoutPlanNotes_ShouldKeepThePlanNotes()
    {
        await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest("A summary.", null, null));

        (await NotesAsync()).ShouldBe("Purpose: set up the trap.");
    }

    [Test]
    [Order(3)]
    public async Task Patch_WhenPlanNotesEmpty_ShouldClearThem()
    {
        await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest(null, null, null, ""));

        (await NotesAsync()).ShouldBeNull();
    }

    [Test]
    [Order(4)]
    public async Task Patch_WhenNothingGiven_400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/scenes/{_sceneId}", new PatchSceneRequest(null, null, null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    private async Task<string?> NotesAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<string?>("SELECT plan_notes FROM scene WHERE id = @_sceneId", new { _sceneId });
    }
}
