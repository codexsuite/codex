using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

[TestFixture]
public class PublicationSettingsEndpointTests : SchemaApiFixture
{
    private async Task<(Guid Series, Guid Book)> InsertAsync(string name)
    {
        await using var db = await OpenDatabaseAsync();
        var series = await TestRows.SeriesAsync(db, name);
        await db.ExecuteAsync(
            """
            INSERT INTO publication_settings (book_id, citation_style, quote_word_limit, publisher_name, planned_year, formats, print_run, territory)
            VALUES (@series, 'apa', 250, 'Series House', 2027, ARRAY['print','ebook'], 5000, 'North America')
            """,
            new { series });
        var book = await TestRows.ShelfBookAsync(db, $"{name} one", null, series, 1, "writing", 1000, 0, null, false);
        return (series, book);
    }

    private Task<PublicationSettingsDto> GetAsync(Guid book) =>
        Client.GetFromJsonAsync<PublicationSettingsDto>($"/api/books/{book}/publication-settings")!;

    private Task<HttpResponseMessage> PatchAsync(Guid book, PatchPublicationSettingsRequest request) =>
        Client.PatchAsJsonAsync($"/api/books/{book}/publication-settings", request);

    [Test]
    public async Task Get_WhenBookHasNoValuesOfItsOwn_ShouldShowSeriesValuesAsInherited()
    {
        var (_, book) = await InsertAsync("Inherit");

        var settings = await GetAsync(book);

        settings.SeriesName.ShouldBe("Inherit");
        (settings.Style.Value, settings.Style.Inherited).ShouldBe(("apa", true));
        (settings.QuoteWordLimit.Value, settings.QuoteWordLimit.Inherited).ShouldBe((250, true));
        settings.Publisher.Value.ShouldBe("Series House");
        settings.PlannedYear.Value.ShouldBe(2027);
        settings.Formats.Value.ShouldBe(["print", "ebook"]);
        settings.PrintRun.Value.ShouldBe(5000);
        settings.Territory.Value.ShouldBe("North America");
    }

    [Test]
    public async Task Patch_WhenValueDiffersFromSeries_ShouldBecomeThisBooksAndLeaveSeriesAlone()
    {
        var (series, book) = await InsertAsync("Own");

        var response = await PatchAsync(book, new PatchPublicationSettingsRequest("harvard", 100, "", 2030, ["audio"], 800, "Australia and New Zealand"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = (await response.Content.ReadFromJsonAsync<PublicationSettingsDto>())!;
        (settings.Style.Value, settings.Style.Inherited).ShouldBe(("harvard", false));
        (settings.QuoteWordLimit.Value, settings.QuoteWordLimit.Inherited).ShouldBe((100, false));
        (settings.Publisher.Value, settings.Publisher.Inherited).ShouldBe(("", false));
        (settings.PlannedYear.Value, settings.PrintRun.Value).ShouldBe((2030, 800));
        settings.Formats.Value.ShouldBe(["audio"]);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT citation_style FROM publication_settings WHERE book_id = @series", new { series }))
            .ShouldBe("apa");
    }

    [Test]
    public async Task Patch_WhenValueEqualsSeries_ShouldGoBackToInheriting()
    {
        var (_, book) = await InsertAsync("Same");
        await PatchAsync(book, new PatchPublicationSettingsRequest("chicago", 100, null, null, ["audio"], null, null));

        var response = await PatchAsync(book, new PatchPublicationSettingsRequest("apa", 250, null, null, ["ebook", "print"], null, null));

        var settings = (await response.Content.ReadFromJsonAsync<PublicationSettingsDto>())!;
        (settings.Style.Inherited, settings.QuoteWordLimit.Inherited, settings.Formats.Inherited).ShouldBe((true, true, true));
    }

    [Test]
    public async Task Delete_WhenFieldIsThisBooks_ShouldRestoreTheSeriesValue()
    {
        var (_, book) = await InsertAsync("Reset");
        await PatchAsync(book, new PatchPublicationSettingsRequest("harvard", 100, null, null, null, null, null));

        var response = await Client.DeleteAsync($"/api/books/{book}/publication-settings/quoteWordLimit");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = (await response.Content.ReadFromJsonAsync<PublicationSettingsDto>())!;
        (settings.QuoteWordLimit.Value, settings.QuoteWordLimit.Inherited).ShouldBe((250, true));
        (settings.Style.Value, settings.Style.Inherited).ShouldBe(("harvard", false));
    }

    [Test]
    public async Task Get_WhenStandaloneWithoutSettings_ShouldAnswerDefaults()
    {
        await using var db = await OpenDatabaseAsync();
        var book = await TestRows.ShelfBookAsync(db, "Alone", null, null, null, "writing", 1000, 0, null, false);

        var settings = await GetAsync(book);

        settings.SeriesName.ShouldBeNull();
        (settings.Style.Value, settings.QuoteWordLimit.Value, settings.Style.Inherited).ShouldBe(("chicago", 300, false));
        settings.Formats.Value.ShouldBeEmpty();
    }

    [Test]
    public async Task Patch_WhenInvalid_ShouldAnswer400()
    {
        var (_, book) = await InsertAsync("Invalid");
        PatchPublicationSettingsRequest[] bad =
        [
            new("mla", null, null, null, null, null, null),
            new(null, 0, null, null, null, null, null),
            new(null, null, null, 1800, null, null, null),
            new(null, null, null, null, ["vinyl"], null, null),
            new(null, null, null, null, null, -1, null),
            new(null, null, null, null, null, null, null),
        ];

        foreach (var request in bad)
        {
            var response = await PatchAsync(book, request);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
        }
        (await GetAsync(book)).Style.Inherited.ShouldBeTrue();
    }

    [Test]
    public async Task Requests_WhenBookOrFieldUnknown_ShouldAnswer404Or400()
    {
        var (_, book) = await InsertAsync("Unknown");

        var missing = await Client.GetAsync($"/api/books/{Guid.NewGuid()}/publication-settings");
        var field = await Client.DeleteAsync($"/api/books/{book}/publication-settings/colour");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        field.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
