using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;

namespace Codex.WebApi.Tests.Manuscript;

[TestFixture]
public class ManuscriptEndpointTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _firstScene;
    private Guid _secondScene;
    private Guid _otherActScene;
    private Guid _noProseScene;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        var act1 = await TestRows.FirstActAsync(db, _bookId);
        var act2 = await TestRows.ActAsync(db, _bookId, "a1", "Second act");
        var ch1 = await TestRows.ChapterAsync(db, _bookId, act1, "a0", "One");
        var ch2 = await TestRows.ChapterAsync(db, _bookId, act2, "a0", "Two");
        // Inserted out of reading order on purpose.
        _otherActScene = await TestRows.SceneAsync(db, _bookId, ch2, "a0");
        _secondScene = await TestRows.SceneAsync(db, _bookId, ch1, "a1");
        _firstScene = await TestRows.SceneAsync(db, _bookId, ch1, "a0");
        _noProseScene = await TestRows.SceneAsync(db, _bookId, ch1, "a2");
        var archivedScene = await TestRows.SceneAsync(db, _bookId, ch1, "a3", archived: true);
        await TestRows.ProseAsync(db, _firstScene, "First.");
        await TestRows.ProseAsync(db, _secondScene, "Second.");
        await TestRows.ProseAsync(db, _otherActScene, "Other act.");
        await TestRows.ProseAsync(db, archivedScene, "Archived.");
    }

    [Test]
    [Order(1)]
    public async Task Get_ShouldReturnLiveScenesInReadingOrderWithBodiesAndVersions()
    {
        var manuscript = await Client.GetFromJsonAsync<ManuscriptDto>($"/api/books/{_bookId}/manuscript", JsonSerializerOptions.Web);

        manuscript!.BookId.ShouldBe(_bookId);
        manuscript.Scenes.Select(s => (s.SceneId, s.Body, s.Version)).ShouldBe(
        [
            (_firstScene, "First.", 1),
            (_secondScene, "Second.", 1),
            (_noProseScene, "", 0),
            (_otherActScene, "Other act.", 1),
        ]);
    }

    [Test]
    [Order(2)]
    public async Task Get_WhenUnknownBook_ShouldReturn404ApiError()
    {
        var response = await Client.GetAsync($"/api/books/{Guid.NewGuid()}/manuscript");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
