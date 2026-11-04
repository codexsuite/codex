using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Settings;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

[TestFixture]
public class BookLifecycleSeriesTests : SchemaApiFixture
{
    [Test]
    public async Task Duplicate_WhenSourceIsInASeries_ShouldTakeTheNextSeriesNumber()
    {
        Guid book;
        Guid series;
        await using (var db = await OpenDatabaseAsync())
        {
            series = await TestRows.SeriesAsync(db, "Copies");
            book = await TestRows.ShelfBookAsync(db, "Copies one", null, series, 1, "writing", 1000, 0, null, false);
            await TestRows.ShelfBookAsync(db, "Copies two", null, series, 2, "writing", 1000, 0, null, false);
        }

        var copy = (await (await Client.PostAsJsonAsync($"/api/books/{book}/duplicate", new DuplicateBookRequest("Copies one again")))
            .Content.ReadFromJsonAsync<DuplicatedBookDto>())!;

        await using var check = await OpenDatabaseAsync();
        var row = await check.QuerySingleAsync<(Guid? SeriesId, int? Number, string Title)>(
            "SELECT series_id, series_number, title FROM book WHERE id = @id", new { id = copy.Id });
        row.ShouldBe((series, 3, "Copies one again"));
    }

    [Test]
    public async Task Duplicate_WhenCopiedConcurrently_ShouldGiveEachCopyItsOwnSeriesNumber()
    {
        Guid book;
        Guid series;
        await using (var db = await OpenDatabaseAsync())
        {
            series = await TestRows.SeriesAsync(db, "Racing copies");
            book = await TestRows.ShelfBookAsync(db, "Racing one", null, series, 1, "writing", 1000, 0, null, false);
        }

        var responses = await Task.WhenAll(Enumerable.Range(1, 4).Select(i =>
            Client.PostAsJsonAsync($"/api/books/{book}/duplicate", new DuplicateBookRequest($"Racing copy {i}"))));

        responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.OK);
        await using var check = await OpenDatabaseAsync();
        var numbers = (await check.QueryAsync<int>(
            "SELECT series_number FROM book WHERE series_id = @series ORDER BY series_number", new { series })).ToList();
        numbers.ShouldBe([1, 2, 3, 4, 5]);
    }

    [Test]
    public async Task Archive_WhenBookIsArchived_ShouldStillOpenItsHeaderAndSettings()
    {
        Guid book;
        await using (var db = await OpenDatabaseAsync())
        {
            book = await TestRows.ShelfBookAsync(db, "Shelved", null, null, null, "writing", 1000, 0, null, false);
        }

        await Client.PostAsync($"/api/books/{book}/archive", null);

        (await Client.GetAsync($"/api/books/{book}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.GetAsync($"/api/books/{book}/publication-settings")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
