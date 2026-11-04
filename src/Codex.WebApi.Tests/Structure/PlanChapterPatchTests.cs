using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

[TestFixture]
public class PlanChapterPatchTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _first;
    private Guid _second;
    private Guid _archived;
    private string? _slug;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        var act = await TestRows.FirstActAsync(db, _bookId);
        _first = await TestRows.ChapterAsync(db, _bookId, act, "a0", "One");
        _second = await TestRows.ChapterAsync(db, _bookId, act, "a1", "Two");
        _archived = await TestRows.ChapterAsync(db, _bookId, act, "a2", "Gone");
        await db.ExecuteAsync("UPDATE chapter SET archived_at = now() WHERE id = @_archived", new { _archived });
        _slug = await db.ExecuteScalarAsync<string>("SELECT slug FROM chapter WHERE id = @_first", new { _first });
    }

    [Test]
    [Order(1)]
    public async Task Patch_WhenGivenTitleSummaryAndNotes_ShouldWriteThemAndKeepTheSlug()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_first}", new PatchChapterRequest("  Renamed ", "Shape", "Notes", null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapter = (await response.Content.ReadFromJsonAsync<OutlineChapterDto>(JsonSerializerOptions.Web))!;
        (chapter.Title, chapter.Slug, chapter.Number).ShouldBe(("Renamed", _slug, 1));
        await using var db = await OpenDatabaseAsync();
        (await db.QuerySingleAsync<(string, string)>("SELECT summary, notes FROM chapter WHERE id = @_first", new { _first })).ShouldBe(("Shape", "Notes"));
    }

    [Test]
    [Order(2)]
    public async Task Patch_WhenIsNumberedFalse_ShouldRenumberTheChaptersAfterIt()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_first}", new PatchChapterRequest(null, null, null, false));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chapters = (await OutlineAsync()).Acts.ShouldHaveSingleItem().Chapters;
        chapters.Single(c => c.Id == _first).Number.ShouldBeNull();
        chapters.Single(c => c.Id == _second).Number.ShouldBe(1);
        chapters.Single(c => c.Id == _first).Title.ShouldBe("Renamed");
    }

    [Test]
    [Order(3)]
    public async Task Patch_WhenSummaryIsEmpty_ShouldClearIt()
    {
        await Client.PatchAsJsonAsync($"/api/chapters/{_first}", new PatchChapterRequest(null, "", null, null));

        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string?>("SELECT summary FROM chapter WHERE id = @_first", new { _first })).ShouldBeNull();
    }

    [Test]
    [Order(4)]
    public async Task Patch_WhenTitleIsBlank_400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_first}", new PatchChapterRequest("   ", null, null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(5)]
    public async Task Patch_WhenTitleIsTooLong_400()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_first}", new PatchChapterRequest(new string('x', 201), null, null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(6)]
    public async Task Patch_WhenChapterIsArchived_404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{_archived}", new PatchChapterRequest("Back", null, null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(7)]
    public async Task Patch_WhenChapterDoesNotExist_404()
    {
        var response = await Client.PatchAsJsonAsync($"/api/chapters/{Guid.NewGuid()}", new PatchChapterRequest("X", null, null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    private async Task<OutlineDto> OutlineAsync()
        => (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web))!;
}
