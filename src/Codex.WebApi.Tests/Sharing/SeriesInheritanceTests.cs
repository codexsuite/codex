using System.Net;
using System.Net.Http.Json;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Codex.Contracts.Settings;
using Codex.Contracts.Sharing;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Sharing;

// A series grant reaches every book in the series, including books added later, and a book that leaves the series
// leaves it behind. In order: JD's "Saga" (book X) is shared with Ana to view as a series and X to edit on its own; JD
// adds book Y; the series grant goes and comes back; Y moves out. "Other Saga" is shared with Bo.
[TestFixture]
public class SeriesInheritanceTests : SchemaApiFixture
{
    private static readonly Guid Ana = new("0199c0de-0000-7000-8000-0000000000d2");
    private static readonly Guid Bo = new("0199c0de-0000-7000-8000-0000000000d3");
    private Guid _saga;
    private Guid _otherSaga;
    private Guid _x;
    private Guid _y;
    private Guid _sagaEntry;
    private HttpClient _ana = null!;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "ana2@example.test", "Ana", id: Ana);
        await TestUsers.CreateAsync(ConnectionString, "bo2@example.test", "Bo", id: Bo);
        await using var db = await OpenDatabaseAsync();
        _saga = await TestRows.SeriesAsync(db, "Saga");
        _otherSaga = await TestRows.SeriesAsync(db, "Other Saga");
        _x = await TestRows.BookAsync(db, "Book X", _saga);
        _sagaEntry = await TestRows.EntryAsync(db, _saga, await TestRows.CategoryAsync(db, _saga, "Places"), "Harbour");
        await TestRows.GrantAsync(db, _otherSaga, Bo, "view");
        (await Client.PutAsJsonAsync($"/api/books/{_saga}/sharing/{Ana}", new SetGrantRequest("view"))).EnsureSuccessStatusCode();
        (await Client.PutAsJsonAsync($"/api/books/{_x}/sharing/{Ana}", new SetGrantRequest("write"))).EnsureSuccessStatusCode();
        _ana = Host.CreateClient().ActingAs(Ana);
    }

    [OneTimeTearDown]
    public void DisposeClient() => _ana.Dispose();

    [Test]
    [Order(1)]
    public async Task CreateBook_WhenTheSeriesIsShared_ShouldReachTheNewBook()
    {
        var created = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("Book Y", null, _saga, null, null, null));
        _y = (await created.Content.ReadFromJsonAsync<CreatedBookDto>())!.Id;

        (await _ana.GetFromJsonAsync<BookHeaderDto>($"/api/books/{_y}"))!.Role.ShouldBe("view");
    }

    [Test]
    [Order(2)]
    public async Task RequireAsync_WhenTheCallerEditsOneBookOnly_ShouldGiveViewNotWriteOnTheSeriesEntries()
    {
        var access = Host.Services.GetRequiredService<IBookAccess>();
        using var _ = CurrentUser.ActAs(Ana);

        (await access.RequireAsync(BookItem.KbEntry, _sagaEntry, BookRole.View)).ShouldBe(_saga);
        (await Should.ThrowAsync<ApiException>(() => access.RequireAsync(BookItem.KbEntry, _sagaEntry, BookRole.Write)))
            .Code.ShouldBe(ApiErrorCodes.Forbidden);
    }

    [Test]
    [Order(3)]
    public async Task DeleteGrant_WhenTheSeriesGrantGoes_ShouldLeaveTheBookGrant()
    {
        (await Client.DeleteAsync($"/api/books/{_saga}/sharing/{Ana}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _ana.GetFromJsonAsync<BookHeaderDto>($"/api/books/{_x}"))!.Role.ShouldBe("write");
        (await (await _ana.GetAsync($"/api/books/{_y}")).ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(4)]
    public async Task MovePreview_WhenTheBookLeavesASharedSeries_ShouldCountWhoLosesAndWhoGains()
    {
        (await Client.PutAsJsonAsync($"/api/books/{_saga}/sharing/{Ana}", new SetGrantRequest("view"))).EnsureSuccessStatusCode();

        var toOther = await PreviewAsync(_y, _otherSaga);
        var toNone = await PreviewAsync(_x, null);

        (toOther.TargetSeriesSharedWith, toOther.LosingAccess).ShouldBe((1, 1));
        (toNone.TargetSeriesSharedWith, toNone.LosingAccess).ShouldBe((0, 0));
    }

    [Test]
    [Order(5)]
    public async Task Move_WhenTheBookLeavesTheSeries_ShouldDropTheInheritedAccess()
    {
        var moved = await Client.PostAsJsonAsync($"/api/books/{_y}/series-move", new SeriesMoveRequest(_otherSaga));

        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await _ana.GetAsync($"/api/books/{_y}")).ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        using var bo = Host.CreateClient().ActingAs(Bo);
        (await bo.GetFromJsonAsync<BookHeaderDto>($"/api/books/{_y}"))!.Role.ShouldBe("view");
    }

    private async Task<SeriesMovePreviewDto> PreviewAsync(Guid bookId, Guid? seriesId)
    {
        var response = await Client.PostAsJsonAsync($"/api/books/{bookId}/series-move/preview", new SeriesMoveRequest(seriesId));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SeriesMovePreviewDto>())!;
    }
}
