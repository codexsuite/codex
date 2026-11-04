using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.References;
using Dapper;

namespace Codex.WebApi.Tests.References;

// J24 steps 1-2: a CSL JSON (Zotero) import, previewed first. The preview counts what the import would do and writes nothing.
[TestFixture]
public class ImportCslAndPreviewTests : SourceApiFixture
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();

    private static ImportSourceItem Item(string? key, string title, string? isbn = null) =>
        new("csl", key, new SourceDetailsRequest("book", title, "Doe, Jane", 2018, "Addison-Wesley", null, isbn, null, null, null, "unknown", null));

    private static readonly ImportSourceItem[] Library =
    [
        Item("fowler2018", "Refactoring", "9780134757599"),
        Item("kim2013", "The Phoenix Project"),
        Item("accel", "Accelerate", "978-1-942788-33-1"),
        Item("blank", "  "),
    ];

    private async Task<ImportSourcesResult> PostAsync(string route)
    {
        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references/{route}", new ImportSourcesRequest(Library));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ImportSourcesResult>())!;
    }

    // The seed's Accelerate has no ISBN; the file's copy does, so the import matches it on the ISBN once it is recorded.
    [OneTimeSetUp]
    public async Task GiveAccelerateAnIsbnAsync()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE reference SET isbn = '9781942788331' WHERE id = @id", new { id = SeedIds.References.Accelerate });
    }

    [Test, Order(1)]
    public async Task Preview_ShouldCountNewDuplicateAndUntitledEntriesAndWriteNothing()
    {
        await using var db = await OpenDatabaseAsync();
        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references/import/preview", new ImportSourcesRequest(Library));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<ImportSourcesResult>())!;

        (result.Created, result.Duplicates, result.Untitled, result.Skipped).ShouldBe((2, 1, 1, 2));
        result.SkippedReasons.ShouldBe(["already in References", "no title"], ignoreOrder: true);
        (await GetSummaryAsync()).Total.ShouldBe(12);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference WHERE external_provider = 'zotero'")).ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task Import_ShouldStoreZoteroProviderAndRecordAnImportEventPerRow()
    {
        await using var db = await OpenDatabaseAsync();
        var result = await PostAsync("import");

        result.Created.ShouldBe(2);
        (await GetSummaryAsync()).Total.ShouldBe(14);
        (await db.QueryAsync<(string Provider, string Key)>(
            "SELECT external_provider, external_key FROM reference WHERE external_provider IS NOT NULL ORDER BY external_key")).ToList()
            .ShouldBe([("zotero", "fowler2018"), ("zotero", "kim2013")]);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_event e JOIN reference r ON r.id = e.reference_id WHERE e.action = 'added' AND e.source = 'import' AND r.external_provider = 'zotero'")).ShouldBe(2);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference r JOIN reference_clearance c ON c.reference_id = r.id WHERE r.external_provider = 'zotero' AND c.status = 'not_cited'")).ShouldBe(2);
    }

    [Test, Order(3)]
    public async Task Import_WhenRunAgain_ShouldSkipEveryEntryByItsKey()
    {
        var result = await PostAsync("import");

        result.Created.ShouldBe(0);
        result.Duplicates.ShouldBeGreaterThanOrEqualTo(3);
        (await GetSummaryAsync()).Total.ShouldBe(14);
    }
}
