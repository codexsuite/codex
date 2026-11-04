using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Dapper;

namespace Codex.WebApi.Tests.Library;

[TestFixture]
public class BookEndpointTests : SchemaApiFixture
{
    private async Task<Guid> InsertSeriesAsync(int liveBooks, bool archivedLast = false)
    {
        await using var db = await OpenDatabaseAsync();
        var series = await TestRows.SeriesAsync(db, $"Series {Guid.NewGuid():N}");
        for (var i = 1; i <= liveBooks; i++)
        {
            await TestRows.ShelfBookAsync(db, $"Book {i}", null, series, i);
        }
        if (archivedLast)
        {
            await TestRows.ShelfBookAsync(db, "Archived", null, series, liveBooks + 1, archived: true);
        }
        return series;
    }

    [Test]
    public async Task Create_WhenStandalone_ShouldAnswer201WithDefaultsAndOneAct()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            await TestRows.EnsureUserAsync(db);
        }

        var response = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("  The Quiet Hours ", null, null, null, 80000, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<CreatedBookDto>())!;
        created.Title.ShouldBe("The Quiet Hours");
        created.Status.ShouldBe("planning");
        created.SeriesNumber.ShouldBeNull();
        await using var check = await OpenDatabaseAsync();
        var acts = await check.QuerySingleAsync<(int Count, string Position)>(
            "SELECT count(*)::int, min(position) FROM act WHERE book_id = @id", new { id = created.Id });
        acts.ShouldBe((1, "a0"));
        (await check.ExecuteScalarAsync<int?>("SELECT target_word_count FROM book WHERE id = @id", new { id = created.Id })).ShouldBe(80000);
    }

    [Test]
    public async Task Create_WhenInSeriesWithoutNumber_ShouldTakeNextAfterArchived()
    {
        var series = await InsertSeriesAsync(2, archivedLast: true);

        var response = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("Fourth", null, series, null, null, "draft"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<CreatedBookDto>())!;
        created.SeriesNumber.ShouldBe(4);
        created.Status.ShouldBe("draft");
    }

    [Test]
    public async Task Create_WhenCreatedTogether_ShouldNumberEachOnce()
    {
        var series = await InsertSeriesAsync(1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(i => Client.PostAsJsonAsync("/api/books", new CreateBookRequest($"Racer {i}", null, series, null, null, null))));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        await using var db = await OpenDatabaseAsync();
        var numbers = await db.QueryAsync<int>("SELECT series_number FROM book WHERE series_id = @series ORDER BY 1", new { series });
        numbers.ShouldBe([1, 2, 3, 4, 5]);
    }

    [Test]
    public async Task Create_WhenTitleBlank_ShouldAnswer400()
    {
        var response = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("   ", null, null, null, null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Create_WhenNumberTaken_ShouldAnswer409AndAddNothing()
    {
        var series = await InsertSeriesAsync(2);

        var response = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("Clash", null, series, 2, null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.Conflict);
        error.Code.ShouldBe(ApiErrorCodes.Conflict);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book WHERE series_id = @series", new { series })).ShouldBe(2);
    }

    [Test]
    public async Task Create_WhenSeriesIdIsABook_ShouldAnswer400()
    {
        Guid book;
        await using (var db = await OpenDatabaseAsync())
        {
            book = await TestRows.BookAsync(db, "Plain book");
        }

        var response = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("Child", null, book, 1, null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Create_WhenNumberWithoutSeries_ShouldAnswer400()
    {
        var response = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("Loose", null, null, 3, null, null));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Create_WhenStatusUnknownOrTargetZero_ShouldAnswer400()
    {
        var badStatus = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("A", null, null, null, null, "archived"));
        var badTarget = await Client.PostAsJsonAsync("/api/books", new CreateBookRequest("B", null, null, null, 0, null));

        (await badStatus.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await badTarget.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task NextNumber_WhenSeriesHasArchivedBook_ShouldCountIt()
    {
        var series = await InsertSeriesAsync(2, archivedLast: true);

        var next = await Client.GetFromJsonAsync<NextSeriesNumberDto>($"/api/series/{series}/next-number");

        next!.NextNumber.ShouldBe(4);
    }

    [Test]
    public async Task NextNumber_WhenNotASeries_ShouldAnswer404()
    {
        var response = await Client.GetAsync($"/api/series/{Guid.NewGuid()}/next-number");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
