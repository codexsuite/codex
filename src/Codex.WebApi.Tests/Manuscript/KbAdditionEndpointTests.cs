using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

[TestFixture]
public class KbAdditionEndpointTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _sceneId;
    private Guid _entryId;
    private readonly Guid _additionId = Guid.NewGuid();

    private string AdditionLine => $"::kb-addition{{id=\"{_additionId}\"}}";

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        await TestRows.ProseAsync(db, _sceneId, "First half.\n\nSecond half.\n");
        _entryId = await TestRows.EntryAsync(db, _bookId, await TestRows.CategoryAsync(db, _bookId, "People"), "Anna");
    }

    private string Body => $"First half.\n\n{AdditionLine}\n\nSecond half.\n";

    [Test]
    [Order(1)]
    public async Task Insert_WhenVersionStale_ShouldReturn409AndLeaveNoRow()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/kb-additions", new InsertKbAdditionRequest(_additionId, _entryId, "She knows.", Body, 7));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_progression WHERE id = @_additionId", new { _additionId })).ShouldBe(0);
    }

    [Test]
    [Order(2)]
    public async Task Insert_WhenSceneUnknown_ShouldReturn404()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{Guid.NewGuid()}/kb-additions", new InsertKbAdditionRequest(_additionId, _entryId, "x", Body, 1));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(3)]
    public async Task Insert_WhenBodyHasNoAdditionLine_ShouldReturn400()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/kb-additions", new InsertKbAdditionRequest(_additionId, _entryId, "x", "First half.\n", 1));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(4)]
    public async Task Insert_WhenEntryIsOutOfScope_ShouldReturn400AndLeaveNoRow()
    {
        await using var db = await OpenDatabaseAsync();
        var other = await TestRows.BookAsync(db, "Elsewhere");
        var foreign = await TestRows.EntryAsync(db, other, await TestRows.CategoryAsync(db, other, "People"), "Stranger");

        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/kb-additions", new InsertKbAdditionRequest(_additionId, foreign, "x", Body, 1));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM kb_progression WHERE id = @_additionId", new { _additionId })).ShouldBe(0);
    }

    [Test]
    [Order(5)]
    public async Task Insert_ShouldCreateTheAdditionAndSaveTheBodyWithItsLine()
    {
        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/kb-additions", new InsertKbAdditionRequest(_additionId, _entryId, "She knows the map.", Body, 1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<InsertKbAdditionResult>(JsonSerializerOptions.Web))!.ShouldBe(new InsertKbAdditionResult(_additionId, 2));
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web))!;
        prose.Body.ShouldContain(AdditionLine);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM kb_progression WHERE id = @_additionId", new { _additionId })).ShouldBeTrue();
    }

    [Test]
    [Order(6)]
    public async Task List_ShouldReturnTheBooksLiveAdditions()
    {
        var additions = (await Client.GetFromJsonAsync<BookKbAdditionsDto>($"/api/books/{_bookId}/kb-additions", JsonSerializerOptions.Web))!;

        additions.Additions.ShouldHaveSingleItem().ShouldBe(new KbAdditionDto(_additionId, _sceneId, _entryId, "She knows the map."));
    }

    [Test]
    [Order(7)]
    public async Task Patch_ShouldChangeTheText()
    {
        var response = await Client.PatchAsJsonAsync($"/api/kb-additions/{_additionId}", new PatchKbAdditionRequest("She has the map.", null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<KbAdditionDto>(JsonSerializerOptions.Web))!.ShouldBe(new KbAdditionDto(_additionId, _sceneId, _entryId, "She has the map."));
    }

    [Test]
    [Order(8)]
    public async Task Patch_WhenEntryIsOutOfScope_ShouldReturn400()
    {
        await using var db = await OpenDatabaseAsync();
        var other = await TestRows.BookAsync(db, "Elsewhere 2");
        var foreign = await TestRows.EntryAsync(db, other, await TestRows.CategoryAsync(db, other, "People"), "Stranger 2");

        var response = await Client.PatchAsJsonAsync($"/api/kb-additions/{_additionId}", new PatchKbAdditionRequest(null, foreign));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(9)]
    public async Task Patch_WhenUnknown_ShouldReturn404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/kb-additions/{Guid.NewGuid()}", new PatchKbAdditionRequest("x", null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(10)]
    public async Task Insert_WhenTheBodyDropsAnExistingAdditionLine_ShouldReturn400()
    {
        var current = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_sceneId}/prose", JsonSerializerOptions.Web))!;
        var fresh = Guid.NewGuid();

        var response = await Client.PostAsJsonAsync($"/api/scenes/{_sceneId}/kb-additions",
            new InsertKbAdditionRequest(fresh, _entryId, "x", $"Only.\n\n::kb-addition{{id=\"{fresh}\"}}\n", current.Version));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }
}
