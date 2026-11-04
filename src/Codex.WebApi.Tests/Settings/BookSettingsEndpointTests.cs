using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

[TestFixture]
public class BookSettingsEndpointTests : SchemaApiFixture
{
    private async Task<(Guid Series, Guid Book)> InsertAsync(string name, int words = 0)
    {
        await using var db = await OpenDatabaseAsync();
        var series = await TestRows.SeriesAsync(db, name);
        await db.ExecuteAsync("UPDATE book SET style_guide = 'Series voice.' WHERE id = @series", new { series });
        await TestRows.ShelfBookAsync(db, $"{name} one", null, series, 1, "published", 1000, 0, null, false);
        var book = await TestRows.ShelfBookAsync(db, $"{name} two", null, series, 2, "writing", 1000, words, null, false);
        return (series, book);
    }

    [Test]
    public async Task Get_WhenInSeries_ShouldAnswerEveryRelevantSection()
    {
        var (series, book) = await InsertAsync("Get", 500);

        var settings = await Client.GetFromJsonAsync<BookSettingsDto>($"/api/books/{book}/settings");

        settings!.Title.ShouldBe("Get two");
        settings.SeriesId.ShouldBe(series);
        settings.SeriesNumber.ShouldBe(2);
        settings.SeriesOrder.Select(o => (o.Title, o.Number)).ShouldBe([("Get one", 1), ("Get two", 2)]);
        settings.SeriesOptions.ShouldContain(o => o.Id == series && o.BookCount == 2);
        settings.SeriesGuide.ShouldBe("Series voice.");
        settings.WordCount.ShouldBe(500);
        settings.ProgressPercent.ShouldBe(50);
    }

    [Test]
    public async Task Patch_WhenFieldsGiven_ShouldChangeOnlyThose()
    {
        var (_, book) = await InsertAsync("Patch");

        var response = await Client.PatchAsJsonAsync($"/api/books/{book}/settings",
            new PatchBookSettingsRequest(" New title ", "Pen Name", "revising", 2000, "Be brief."));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = (await response.Content.ReadFromJsonAsync<BookSettingsDto>())!;
        (settings.Title, settings.AuthorName, settings.Status, settings.TargetWords, settings.StyleGuide)
            .ShouldBe(("New title", "Pen Name", "revising", 2000, "Be brief."));

        var cleared = await Client.PatchAsJsonAsync($"/api/books/{book}/settings",
            new PatchBookSettingsRequest(null, "", null, 0, ""));
        var after = (await cleared.Content.ReadFromJsonAsync<BookSettingsDto>())!;
        (after.Title, after.AuthorName, after.TargetWords, after.StyleGuide).ShouldBe(("New title", null, null, null));
    }

    [Test]
    public async Task Patch_WhenSubtitleAndDescriptionGiven_ShouldStoreThemAndTheEmptyStringClearsThem()
    {
        var (_, book) = await InsertAsync("Sub");

        var set = await Client.PatchAsJsonAsync($"/api/books/{book}/settings",
            new PatchBookSettingsRequest(null, null, null, null, null, " A subtitle ", "About the book."));
        var settings = (await set.Content.ReadFromJsonAsync<BookSettingsDto>())!;
        (settings.Subtitle, settings.Description, settings.Title).ShouldBe(("A subtitle", "About the book.", "Sub two"));

        var cleared = await Client.PatchAsJsonAsync($"/api/books/{book}/settings",
            new PatchBookSettingsRequest(null, null, null, null, null, "", null));
        var after = (await cleared.Content.ReadFromJsonAsync<BookSettingsDto>())!;
        (after.Subtitle, after.Description).ShouldBe((null, "About the book."));
    }

    [Test]
    public async Task Patch_WhenInvalid_ShouldAnswer400AndChangeNothing()
    {
        var (_, book) = await InsertAsync("Invalid");

        foreach (var request in new[]
        {
            new PatchBookSettingsRequest("  ", null, null, null, null),
            new PatchBookSettingsRequest(null, null, "finished", null, null),
            new PatchBookSettingsRequest(null, null, null, -5, null),
            new PatchBookSettingsRequest(null, null, null, null, null),
        })
        {
            var response = await Client.PatchAsJsonAsync($"/api/books/{book}/settings", request);
            (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        }

        (await Client.GetFromJsonAsync<BookSettingsDto>($"/api/books/{book}/settings"))!.Title.ShouldBe("Invalid two");
    }

    [Test]
    public async Task Settings_WhenBookUnknown_ShouldAnswer404()
    {
        var get = await Client.GetAsync($"/api/books/{Guid.NewGuid()}/settings");
        (await get.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);

        var patch = await Client.PatchAsJsonAsync($"/api/books/{Guid.NewGuid()}/settings",
            new PatchBookSettingsRequest("x", null, null, null, null));
        (await patch.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
