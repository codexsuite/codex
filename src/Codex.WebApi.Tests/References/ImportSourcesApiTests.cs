using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// Import into the seed book: new entries are created, anything already there (or repeated in the file) is skipped.
[TestFixture]
public class ImportSourcesApiTests : SourceApiFixture
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();

    private static ImportSourceItem Item(string provider, string? key, string title, string? isbn = null, string? doi = null, string? url = null) =>
        new(provider, key, new SourceDetailsRequest("book", title, "Doe, Jane", 2001, null, null, isbn, doi, url, null, "unknown", null));

    private async Task<ImportSourcesResult> ImportAsync(params ImportSourceItem[] items)
    {
        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references/import", new ImportSourcesRequest(items));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ImportSourcesResult>())!;
    }

    [Test, Order(1)]
    public async Task Import_WhenEntriesAreNew_ShouldCreateThemWithTheirKeys()
    {
        var before = (await GetSummaryAsync()).Total;

        var result = await ImportAsync(
            Item("bibtex", "doe2001", "First imported", isbn: "9780140449136"),
            Item("ris", "R1", "Second imported", doi: "10.5555/abc"));

        (result.Created, result.Skipped).ShouldBe((2, 0));
        (await GetSummaryAsync()).Total.ShouldBe(before + 2);
        await using var db = await OpenDatabaseAsync();
        var rows = (await Dapper.SqlMapper.QueryAsync<(string Provider, string Key, string? CiteKey)>(
            db, "SELECT external_provider, external_key, cite_key FROM reference WHERE title LIKE '%imported' ORDER BY title")).ToList();
        rows.ShouldBe([("bibtex", "doe2001", "doe2001"), ("ris", "R1", null)]);
    }

    [Test, Order(2)]
    public async Task Import_WhenEntriesAreAlreadyThere_ShouldSkipThemAndCreateTheRest()
    {
        var before = (await GetSummaryAsync()).Total;

        var result = await ImportAsync(
            Item("bibtex", "doe2001", "Same key again"),
            Item("bibtex", null, "Same ISBN again", isbn: "978-0-14-044913-6"),
            Item("ris", null, "Same DOI, other case", doi: "10.5555/ABC"),
            Item("ris", "R1", "Same RIS id"),
            Item("bibtex", "beck1999", "Collides with a seeded cite key"),
            Item("bibtex", "fresh2002", "A new one"),
            Item("bibtex", "fresh2002", "Repeated in the same file"));

        (result.Created, result.Skipped).ShouldBe((1, 6));
        result.SkippedTitles.ShouldContain("Same ISBN again");
        (await GetSummaryAsync()).Total.ShouldBe(before + 1);
    }

    [Test, Order(3)]
    public async Task Import_WhenAnEntryHasNoTitle_ShouldSkipItAsUntitled()
    {
        var result = await ImportAsync(Item("bibtex", "notitle", "  "));

        result.Created.ShouldBe(0);
        result.SkippedTitles.ShouldBe(["(untitled entry)"]);
    }

    [Test, Order(4)]
    public async Task Import_WhenAFormatIsUnknown_ShouldBe400AndCreateNothing()
    {
        var before = (await GetSummaryAsync()).Total;

        using var response = await Client.PostAsJsonAsync(
            $"/api/books/{Book}/references/import", new ImportSourcesRequest([Item("bibtex", "ok2003", "Fine"), Item("zotero", "x", "Bad format")]));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await GetSummaryAsync()).Total.ShouldBe(before);
    }

    [Test, Order(5)]
    public async Task Import_WhenThereAreTooManyEntries_ShouldBe400()
    {
        var items = Enumerable.Range(0, 501).Select(i => Item("bibtex", $"k{i}", $"Entry {i}")).ToList();

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references/import", new ImportSourcesRequest(items));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(6)]
    public async Task Import_WhenTheBookIsUnknown_ShouldBe404()
    {
        using var response = await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/references/import", new ImportSourcesRequest([]));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(7)]
    public async Task Import_WhenTheBodyIsOverTwoMegabytes_ShouldBe413()
    {
        var item = Item("bibtex", "big", new string('x', 2_200_000));

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references/import", new ImportSourcesRequest([item]));

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }
}
