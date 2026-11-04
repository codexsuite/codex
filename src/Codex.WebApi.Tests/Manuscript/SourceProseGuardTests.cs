using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

// Every editor save is checked, Rich or Source: an id for a beat, addition, figure or source
// reference may be kept, removed or point at a row of this scene (archived or not), never at one that is not the
// scene's (the MCP rule, relaxed for the scene's own rows so cut-and-paste of a ::beat line works).
[TestFixture]
public class SourceProseGuardTests : ApiFixture
{
    private Guid _sceneId;
    private Guid _beatId;
    private Guid _otherBeatId;
    private Guid _foreignBeatId;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        _beatId = await TestRows.BeatAsync(db, _sceneId);
        _otherBeatId = await TestRows.BeatAsync(db, _sceneId);
        var (bookId, chapterId, _) = (await db.QuerySingleAsync<(Guid, Guid, Guid)>(
            "SELECT book_id, chapter_id, id FROM scene WHERE id = @id", new { id = _sceneId }));
        _foreignBeatId = await TestRows.BeatAsync(db, await TestRows.SceneAsync(db, bookId, chapterId, "a1"));
        await TestRows.ProseAsync(db, _sceneId, $"Opening.\n\n::beat{{id=\"{_beatId}\"}}\n\nThe beat's prose.\n");
    }

    private Task<HttpResponseMessage> SaveAsync(string body, int version) =>
        Client.PutAsJsonAsync($"/api/scenes/{_sceneId}/prose", new SaveProseRequest(body, version));

    [Test]
    [Order(1)]
    public async Task Put_WhenSourceInventsABeat_ShouldRefuseWithAWebMessage()
    {
        var response = await SaveAsync($"Opening.\n\n::beat{{id=\"{Guid.NewGuid()}\"}}\n", 1);

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
        error.Error.ShouldContain("/ menu");
        error.Error.ShouldNotContain("upsert_beat");
    }

    [Test]
    [Order(2)]
    public async Task Put_WhenSourceInventsARef_ShouldRefuse()
    {
        var response = await SaveAsync($"Opening [quoted](ref:{Guid.NewGuid()}).\n\n::beat{{id=\"{_beatId}\"}}\n", 1);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(3)]
    public async Task Put_WhenSourceEditsAroundTheBeat_ShouldSave()
    {
        var response = await SaveAsync($"Opening, edited in Source.\n\n::beat{{id=\"{_beatId}\"}}\n\nThe beat's prose.\n", 1);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SaveProseResult>(JsonSerializerOptions.Web))!.Version.ShouldBe(2);
    }

    [Test]
    [Order(4)]
    public async Task Put_WhenRichNamesTheScenesOwnBeat_ShouldSave()
    {
        var response = await SaveAsync($"Opening.\n\n::beat{{id=\"{_beatId}\"}}\n\n::beat{{id=\"{_otherBeatId}\"}}\n", 2);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    [Order(5)]
    public async Task Put_WhenAnUnflaggedSaveInventsABeat_ShouldRefuse()
    {
        // What a kept draft does after a remount: the flag is gone, the text is the same.
        var response = await SaveAsync($"Opening.\n\n::beat{{id=\"{Guid.NewGuid()}\"}}\n", 3);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("/ menu");
    }

    [Test]
    [Order(6)]
    public async Task Put_WhenRawLatexBlockHoldsAnInventedBeat_ShouldRefuse()
    {
        var response = await SaveAsync($"Opening.\n\n```{{{{=latex}}}}\n::beat{{id=\"{Guid.NewGuid()}\"}}\n```\n", 3);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("/ menu");
    }

    [Test]
    [Order(7)]
    public async Task Put_WhenInlineRawLatexHoldsAnInventedRef_ShouldRefuse()
    {
        var response = await SaveAsync($"Opening `[x](ref:{Guid.NewGuid()})`{{{{=latex}}}} end.\n", 3);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldContain("Source reference");
    }

    [Test]
    [Order(8)]
    public async Task Put_WhenTheBeatBelongsToAnotherScene_ShouldRefuse()
    {
        var response = await SaveAsync($"Opening.\n\n::beat{{id=\"{_foreignBeatId}\"}}\n", 3);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(9)]
    public async Task Put_WhenABeatLineIsCutThenPastedBack_ShouldSaveBoth()
    {
        var cut = await SaveAsync("Opening.\n", 3);
        var pasted = await SaveAsync($"Opening.\n\n::beat{{id=\"{_beatId}\"}}\n", 4);

        cut.StatusCode.ShouldBe(HttpStatusCode.OK);
        pasted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
