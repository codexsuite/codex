using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

[TestFixture]
public class SeriesNumberEndpointTests : SchemaApiFixture
{
    private async Task<(Guid Series, Guid[] Books)> InsertAsync(string name, int count, bool archivedExtra = false)
    {
        await using var db = await OpenDatabaseAsync();
        var series = await TestRows.SeriesAsync(db, name);
        var books = new List<Guid>();
        for (var i = 1; i <= count; i++)
        {
            books.Add(await TestRows.ShelfBookAsync(db, $"{name} {i}", null, series, i, "planning", null, 0, null, false));
        }
        if (archivedExtra)
        {
            await TestRows.ShelfBookAsync(db, $"{name} archived", null, series, count + 1, "planning", null, 0, null, true);
        }
        return (series, books.ToArray());
    }

    private async Task<Dictionary<Guid, int>> NumbersAsync(Guid series)
    {
        await using var db = await OpenDatabaseAsync();
        var rows = await db.QueryAsync<(Guid Id, int Number)>("SELECT id, series_number FROM book WHERE series_id = @series", new { series });
        return rows.ToDictionary(r => r.Id, r => r.Number);
    }

    [Test]
    public async Task Number_WhenChangedAndUndone_ShouldShiftOthersAndRestore()
    {
        var (series, books) = await InsertAsync("Shift", 3, archivedExtra: true);

        var response = await Client.PutAsJsonAsync($"/api/books/{books[2]}/series-number", new SetSeriesNumberRequest(1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var change = (await response.Content.ReadFromJsonAsync<SeriesNumberChangeDto>())!;
        change.PreviousNumber.ShouldBe(3);
        change.Books.Select(b => (b.Id, b.Number, b.PreviousNumber))
            .ShouldBe([(books[2], 1, 3), (books[0], 2, 1), (books[1], 3, 2)]);
        var numbers = await NumbersAsync(series);
        (numbers[books[2]], numbers[books[0]], numbers[books[1]]).ShouldBe((1, 2, 3));
        numbers.Values.Order().ShouldBe([1, 2, 3, 4]);

        var undo = await Client.PutAsJsonAsync($"/api/books/{books[2]}/series-number", new SetSeriesNumberRequest(change.PreviousNumber));

        undo.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = await NumbersAsync(series);
        books.Select((id, i) => restored[id] == i + 1).ShouldAllBe(ok => ok);
    }

    [Test]
    public async Task Number_WhenOutOfRange_ShouldAnswer400AndChangeNothing()
    {
        var (series, books) = await InsertAsync("Range", 2, archivedExtra: true);

        foreach (var number in new[] { 0, -1, 3, 4, 99 })
        {
            var response = await Client.PutAsJsonAsync($"/api/books/{books[0]}/series-number", new SetSeriesNumberRequest(number));
            (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        }
        (await NumbersAsync(series))[books[0]].ShouldBe(1);
    }

    [Test]
    public async Task Number_WhenStandaloneOrUnknown_ShouldBeRejected()
    {
        Guid standalone;
        await using (var db = await OpenDatabaseAsync())
        {
            standalone = await TestRows.ShelfBookAsync(db, "Alone");
        }

        var alone = await Client.PutAsJsonAsync($"/api/books/{standalone}/series-number", new SetSeriesNumberRequest(1));
        (await alone.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);

        var unknown = await Client.PutAsJsonAsync($"/api/books/{Guid.NewGuid()}/series-number", new SetSeriesNumberRequest(1));
        (await unknown.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Number_WhenChangedConcurrently_ShouldNeverFailAndStayOneToN()
    {
        var (series, books) = await InsertAsync("Race", 4);

        var responses = await Task.WhenAll(books.Select((id, i) =>
            Client.PutAsJsonAsync($"/api/books/{id}/series-number", new SetSeriesNumberRequest(4 - i))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        (await NumbersAsync(series)).Values.Order().ShouldBe([1, 2, 3, 4]);
    }
}
