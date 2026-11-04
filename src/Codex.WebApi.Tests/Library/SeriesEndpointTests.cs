using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Dapper;

namespace Codex.WebApi.Tests.Library;

[TestFixture]
public class SeriesEndpointTests : SchemaApiFixture
{
    private async Task<(Guid Series, Guid[] Books)> InsertSeriesAsync(string title, int bookCount, bool archivedExtra = false)
    {
        await using var db = await OpenDatabaseAsync();
        var series = await TestRows.SeriesAsync(db, title, "Old description");
        var books = new List<Guid>();
        for (var i = 1; i <= bookCount; i++)
        {
            books.Add(await TestRows.ShelfBookAsync(db, $"{title} {i}", null, series, i, "planning", null, 0, null, false));
        }
        if (archivedExtra)
        {
            await TestRows.ShelfBookAsync(db, $"{title} archived", null, series, bookCount + 1, "planning", null, 0, null, true);
        }
        return (series, books.ToArray());
    }

    private async Task<Dictionary<Guid, int>> NumbersAsync(Guid series)
    {
        await using var db = await OpenDatabaseAsync();
        var rows = await db.QueryAsync<(Guid Id, int Number)>(
            "SELECT id, series_number FROM book WHERE series_id = @series", new { series });
        return rows.ToDictionary(r => r.Id, r => r.Number);
    }

    [Test]
    public async Task Order_WhenReversed_ShouldRenumberInOneGo()
    {
        var (series, books) = await InsertSeriesAsync("Reorder", 3, archivedExtra: true);
        var reversed = books.Reverse().ToArray();

        var response = await Client.PutAsJsonAsync($"/api/series/{series}/order", new SetSeriesOrderRequest(reversed));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<SeriesOrderDto>();
        order!.Books.Select(b => (b.Id, b.SeriesNumber)).ShouldBe(reversed.Select((id, i) => (id, i + 1)));
        var numbers = await NumbersAsync(series);
        reversed.Select((id, i) => numbers[id] == i + 1).ShouldAllBe(ok => ok);
        numbers.Values.Order().ShouldBe([1, 2, 3, 4]);
    }

    [Test]
    public async Task Order_WhenBookMissing_ShouldAnswer400AndChangeNothing()
    {
        var (series, books) = await InsertSeriesAsync("Missing", 2);

        var response = await Client.PutAsJsonAsync($"/api/series/{series}/order", new SetSeriesOrderRequest([books[1]]));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
        (await NumbersAsync(series))[books[0]].ShouldBe(1);
    }

    [Test]
    public async Task Order_WhenForeignBook_ShouldAnswer400()
    {
        var (series, books) = await InsertSeriesAsync("Own", 2);
        var (_, others) = await InsertSeriesAsync("Foreign", 1);

        var response = await Client.PutAsJsonAsync($"/api/series/{series}/order", new SetSeriesOrderRequest([books[0], others[0]]));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Order_WhenDuplicateId_ShouldAnswer400()
    {
        var (series, books) = await InsertSeriesAsync("Dup", 2);

        var response = await Client.PutAsJsonAsync($"/api/series/{series}/order", new SetSeriesOrderRequest([books[0], books[0]]));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Order_WhenReorderedConcurrently_ShouldNeverFailAndEndWithOneOfTheOrders()
    {
        var (series, books) = await InsertSeriesAsync("Race", 4, archivedExtra: true);
        var forward = books;
        var backward = books.Reverse().ToArray();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Client.PutAsJsonAsync($"/api/series/{series}/order", new SetSeriesOrderRequest(i % 2 == 0 ? forward : backward))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        var numbers = await NumbersAsync(series);
        numbers.Values.Order().ShouldBe([1, 2, 3, 4, 5]);
        var ascending = forward.Select(id => numbers[id]).ToArray();
        (ascending.SequenceEqual([1, 2, 3, 4]) || ascending.SequenceEqual([4, 3, 2, 1])).ShouldBeTrue();
    }

    [Test]
    public async Task Order_WhenNotASeries_ShouldAnswer404()
    {
        var (_, books) = await InsertSeriesAsync("NotSeries", 1);

        var response = await Client.PutAsJsonAsync($"/api/series/{books[0]}/order", new SetSeriesOrderRequest([]));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.NotFound);
        error.Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Patch_WhenRenamed_ShouldReturnSeriesAndPersist()
    {
        var (series, _) = await InsertSeriesAsync("Rename", 1);

        var response = await Client.PatchAsJsonAsync($"/api/series/{series}", new PatchSeriesRequest("  Renamed  ", ""));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<SeriesDto>();
        dto.ShouldBe(new SeriesDto(series, "Renamed", null));
    }

    [Test]
    public async Task Patch_WhenNameBlank_ShouldAnswer400AndKeepName()
    {
        var (series, _) = await InsertSeriesAsync("Keep", 1);

        var response = await Client.PatchAsJsonAsync($"/api/series/{series}", new PatchSeriesRequest("   ", null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT title FROM book WHERE id = @series", new { series })).ShouldBe("Keep");
    }

    [Test]
    public async Task Patch_WhenNotASeries_ShouldAnswer404()
    {
        var (_, books) = await InsertSeriesAsync("PatchBook", 1);

        var response = await Client.PatchAsJsonAsync($"/api/series/{books[0]}", new PatchSeriesRequest("X", null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.NotFound);
        error.Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
