using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Library;
using Codex.Contracts.Settings;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

[TestFixture]
public class SeriesMoveEndpointTests : SchemaApiFixture
{
    private async Task<(Guid Old, Guid Target, Guid[] OldBooks, Guid[] TargetBooks)> InsertAsync(string name)
    {
        await using var db = await OpenDatabaseAsync();
        var old = await TestRows.SeriesAsync(db, $"{name} old");
        var target = await TestRows.SeriesAsync(db, $"{name} target");
        var oldBooks = new List<Guid>();
        for (var i = 1; i <= 3; i++)
        {
            oldBooks.Add(await TestRows.ShelfBookAsync(db, $"{name} old {i}", null, old, i, "planning", null, 0, null, false));
        }
        var targetBook = await TestRows.ShelfBookAsync(db, $"{name} target 1", null, target, 1, "planning", null, 0, null, false);
        var category = await TestRows.CategoryAsync(db, old, "Spells");
        await TestRows.EntryAsync(db, old, category, "Fireball");
        await TestRows.LabelAsync(db, old, "Playtested");
        return (old, target, oldBooks.ToArray(), [targetBook]);
    }

    private async Task<(Guid? Series, int? Number)> PlaceAsync(Guid book)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.QuerySingleAsync<(Guid? Series, int? Number)>("SELECT series_id, series_number FROM book WHERE id = @book", new { book });
    }

    [Test]
    public async Task Move_WhenLeavingSeries_ShouldCopyKbDownAndCloseTheGap()
    {
        var (old, _, books, _) = await InsertAsync("Leave");

        var response = await Client.PostAsJsonAsync($"/api/books/{books[0]}/series-move", new SeriesMoveRequest(null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = (await response.Content.ReadFromJsonAsync<BookSettingsDto>())!;
        settings.SeriesId.ShouldBeNull();
        var kb = (await Client.GetFromJsonAsync<KbDto>($"/api/books/{books[0]}/kb"))!;
        kb.Categories.ShouldContain(c => c.Name == "Spells" && !c.FromSeries);
        kb.Entries.ShouldContain(e => e.Name == "Fireball");
        (await PlaceAsync(books[1])).ShouldBe((old, 1));
        (await PlaceAsync(books[2])).ShouldBe((old, 2));
    }

    [Test]
    public async Task Move_WhenSwitchingSeries_ShouldJoinAsLastBookAndMergeLabels()
    {
        var (old, target, books, targetBooks) = await InsertAsync("Switch");
        await using (var db = await OpenDatabaseAsync())
        {
            await TestRows.LabelAsync(db, target, "Playtested");
        }

        var preview = await Client.PostAsJsonAsync($"/api/books/{books[1]}/series-move/preview", new SeriesMoveRequest(target));
        var shown = (await preview.Content.ReadFromJsonAsync<SeriesMovePreviewDto>())!;
        shown.Steps.Select(s => s.Key).ShouldBe(["copyCategories", "copyEntries", "copyLabels", "repoint", "keepSettings", "join", "renumber"]);
        shown.To.Number.ShouldBe(2);
        (await PlaceAsync(books[1])).ShouldBe((old, 2));

        var response = await Client.PostAsJsonAsync($"/api/books/{books[1]}/series-move", new SeriesMoveRequest(target));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlaceAsync(books[1])).ShouldBe((target, 2));
        (await PlaceAsync(targetBooks[0])).ShouldBe((target, 1));
        (await PlaceAsync(books[2])).ShouldBe((old, 2));
        var kb = (await Client.GetFromJsonAsync<KbDto>($"/api/books/{books[1]}/kb"))!;
        kb.Categories.ShouldContain(c => c.Name == "Spells" && !c.FromSeries);
    }

    [Test]
    public async Task Move_WhenStandaloneJoinsSeries_ShouldTakeNextNumber()
    {
        var (_, target, _, _) = await InsertAsync("Join");
        Guid alone;
        await using (var db = await OpenDatabaseAsync())
        {
            alone = await TestRows.ShelfBookAsync(db, "Alone");
        }

        var response = await Client.PostAsJsonAsync($"/api/books/{alone}/series-move", new SeriesMoveRequest(target));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlaceAsync(alone)).ShouldBe((target, 2));
    }

    [Test]
    public async Task Move_WhenTargetIsNotASeries_ShouldAnswer400()
    {
        var (_, _, books, targetBooks) = await InsertAsync("Bad");

        foreach (var id in new[] { targetBooks[0], Guid.NewGuid() })
        {
            var preview = await Client.PostAsJsonAsync($"/api/books/{books[0]}/series-move/preview", new SeriesMoveRequest(id));
            (await preview.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
            var move = await Client.PostAsJsonAsync($"/api/books/{books[0]}/series-move", new SeriesMoveRequest(id));
            (await move.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        }
        (await PlaceAsync(books[0])).Number.ShouldBe(1);
    }

    [Test]
    public async Task Move_WhenAlreadyThereOrUnknownBook_ShouldBeRejected()
    {
        var (old, _, books, _) = await InsertAsync("Same");

        var same = await Client.PostAsJsonAsync($"/api/books/{books[0]}/series-move", new SeriesMoveRequest(old));
        (await same.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);

        var unknown = await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/series-move", new SeriesMoveRequest(null));
        (await unknown.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Move_WhenOppositeMovesAndReordersRace_ShouldNeverFailWithAServerError()
    {
        var (first, second, firstBooks, _) = await InsertAsync("RaceA");
        Guid[] secondBooks;
        await using (var db = await OpenDatabaseAsync())
        {
            secondBooks = [
                await TestRows.ShelfBookAsync(db, "RaceB 1", null, second, 2, "planning", null, 0, null, false),
                await TestRows.ShelfBookAsync(db, "RaceB 2", null, second, 3, "planning", null, 0, null, false)];
        }

        var calls = new List<Task<HttpResponseMessage>>
        {
            Client.PostAsJsonAsync($"/api/books/{firstBooks[0]}/series-move", new SeriesMoveRequest(second)),
            Client.PostAsJsonAsync($"/api/books/{secondBooks[0]}/series-move", new SeriesMoveRequest(first)),
            Client.PostAsJsonAsync($"/api/books/{firstBooks[1]}/series-move", new SeriesMoveRequest(second)),
            Client.PostAsJsonAsync($"/api/books/{secondBooks[1]}/series-move", new SeriesMoveRequest(first)),
            Client.PutAsJsonAsync($"/api/series/{first}/order", new SetSeriesOrderRequest(firstBooks.Reverse().ToArray())),
            Client.PutAsJsonAsync($"/api/series/{second}/order", new SetSeriesOrderRequest(secondBooks)),
            Client.PutAsJsonAsync($"/api/books/{firstBooks[2]}/series-number", new SetSeriesNumberRequest(1)),
        };
        var responses = await Task.WhenAll(calls);

        responses.ShouldAllBe(r => (int)r.StatusCode < 500);
        responses.Take(4).ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
        await using var check = await OpenDatabaseAsync();
        foreach (var series in new[] { first, second })
        {
            var numbers = (await check.QueryAsync<int>(
                "SELECT series_number FROM book WHERE series_id = @series AND archived_at IS NULL ORDER BY series_number", new { series })).ToList();
            numbers.ShouldBe(Enumerable.Range(1, numbers.Count).ToList());
        }
    }
}
