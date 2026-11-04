using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Books;
using Dapper;

namespace Codex.WebApi.Tests.Library;

// An archived series (archived_at on its row, books untouched) takes its books off the shelf with it; restoring
// brings them back as they were, and a book archived on its own stays archived.
[TestFixture]
public class SeriesArchiveLibraryTests : SchemaApiFixture
{
    private Guid _series;
    private Guid _live;
    private Guid _own;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _series = await TestRows.SeriesAsync(db, "Shelved Saga");
        _live = await TestRows.ShelfBookAsync(db, "Saga One", null, _series, 1);
        _own = await TestRows.ShelfBookAsync(db, "Saga Two", null, _series, 2, archived: true);
        await TestRows.ShelfBookAsync(db, "Bystander");
    }

    private async Task SetSeriesArchivedAsync(bool archived)
    {
        var response = await Client.PostAsync($"/api/series/{_series}/{(archived ? "archive" : "restore")}", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<LibraryDto> LibraryAsync(string query = "") =>
        (await Client.GetFromJsonAsync<LibraryDto>($"/api/library{query}"))!;

    private static List<string> Titles(LibraryDto library) =>
        [.. library.Series.SelectMany(s => s.Books).Concat(library.Standalone).Select(b => b.Title)];

    [Test]
    [Order(1)]
    public async Task Get_WhenSeriesArchivedWithBooks_ShouldHideThemFromTheShelfAndListThemAsArchived()
    {
        await SetSeriesArchivedAsync(true);

        var shelf = await LibraryAsync();
        shelf.Series.ShouldNotContain(s => s.Id == _series);
        Titles(shelf).ShouldBe(["Bystander"]);

        var archived = await LibraryAsync("?status=archived");
        Titles(archived).OrderBy(t => t).ShouldBe(["Saga One", "Saga Two"]);
        (archived.Series.SelectMany(s => s.Books).Concat(archived.Standalone)).ShouldAllBe(b => b.Archived);
        // The archived series is one row of its own, carrying its books.
        var row = archived.Series.Where(s => s.Id == _series).ShouldHaveSingleItem();
        row.Archived.ShouldBeTrue();
        row.Books.Count.ShouldBe(2);
        row.BookCount.ShouldBe(2);
    }

    [Test]
    [Order(2)]
    public async Task Get_WhenSeriesRestored_ShouldBringBackItsBooksAndKeepOneArchivedOnItsOwnArchived()
    {
        await SetSeriesArchivedAsync(false);

        var shelf = await LibraryAsync();
        var series = shelf.Series.Where(s => s.Id == _series).ShouldHaveSingleItem();
        var book = series.Books.ShouldHaveSingleItem();
        book.Id.ShouldBe(_live);
        book.SeriesNumber.ShouldBe(1);
        var archived = await LibraryAsync("?status=archived");
        archived.Standalone.Concat(archived.Series.SelectMany(s => s.Books)).ShouldHaveSingleItem().Id.ShouldBe(_own);
    }

    [Test]
    [Order(3)]
    public async Task Restore_WhenSeriesIsNotArchived_ShouldAnswer404()
    {
        var response = await Client.PostAsync($"/api/series/{_series}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(4)]
    public async Task Archive_WhenSeriesIsLive_ShouldReturnTheSeriesAndKeepItsBooksNumbered()
    {
        var response = await Client.PostAsync($"/api/series/{_series}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Codex.Contracts.Library.SeriesDto>())!.Name.ShouldBe("Shelved Saga");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT series_number FROM book WHERE id = @id", new { id = _live })).ShouldBe(1);
        (await Client.PostAsync($"/api/series/{_series}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await SetSeriesArchivedAsync(false);
    }
}
