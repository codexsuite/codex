using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Library;
using Dapper;

namespace Codex.WebApi.Tests.Library;

[TestFixture]
public class SeriesLifecycleEndpointTests : SchemaApiFixture
{
    private async Task<Guid> StandaloneAsync(string title, bool archived = false)
    {
        await using var db = await OpenDatabaseAsync();
        return await TestRows.ShelfBookAsync(db, title, archived: archived);
    }

    [Test]
    public async Task Create_WhenBooksPicked_ShouldNumberThemInPickedOrder()
    {
        var first = await StandaloneAsync("Picked first");
        var second = await StandaloneAsync("Picked second");

        var response = await Client.PostAsJsonAsync("/api/series", new CreateSeriesRequest(" Editions ", [second, first]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<CreatedSeriesDto>())!;
        created.Name.ShouldBe("Editions");
        await using var db = await OpenDatabaseAsync();
        var rows = (await db.QueryAsync<(Guid Id, Guid? SeriesId, int? Number)>(
            "SELECT id, series_id, series_number FROM book WHERE id = ANY(@ids)", new { ids = new[] { first, second } })).ToList();
        rows.Single(r => r.Id == second).Number.ShouldBe(1);
        rows.Single(r => r.Id == first).Number.ShouldBe(2);
        rows.ShouldAllBe(r => r.SeriesId == created.Id);
    }

    [Test]
    public async Task Create_WhenNoBooks_ShouldMakeAnEmptySeries()
    {
        var response = await Client.PostAsJsonAsync("/api/series", new CreateSeriesRequest("Empty one", []));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Create_WhenDescriptionAndAuthorGiven_ShouldStoreThemTrimmed()
    {
        var response = await Client.PostAsJsonAsync("/api/series", new CreateSeriesRequest("Described", [], " A saga. ", " J. Dickson "));

        var created = (await response.Content.ReadFromJsonAsync<CreatedSeriesDto>())!;
        await using var db = await OpenDatabaseAsync();
        var row = await db.QuerySingleAsync<(string? Description, string? Author)>(
            "SELECT description, author_name FROM book WHERE id = @id", new { id = created.Id });
        row.ShouldBe(("A saga.", "J. Dickson"));
    }

    [Test]
    public async Task Create_WhenNameTooLong_ShouldAnswer400()
    {
        var response = await Client.PostAsJsonAsync("/api/series", new CreateSeriesRequest(new string('x', 201), []));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Create_WhenNameBlank_ShouldAnswer400AndWriteNothing()
    {
        await using var before = await OpenDatabaseAsync();
        var count = await before.ExecuteScalarAsync<long>("SELECT count(*) FROM book WHERE kind = 'series'");

        var response = await Client.PostAsJsonAsync("/api/series", new CreateSeriesRequest("  ", []));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
        (await before.ExecuteScalarAsync<long>("SELECT count(*) FROM book WHERE kind = 'series'")).ShouldBe(count);
    }

    [Test]
    public async Task Create_WhenBookAlreadyInASeriesOrArchived_ShouldAnswer400()
    {
        var archived = await StandaloneAsync("Archived pick", archived: true);
        var created = await Client.PostAsJsonAsync("/api/series", new CreateSeriesRequest("First", []));
        var seriesId = (await created.Content.ReadFromJsonAsync<CreatedSeriesDto>())!.Id;
        Guid member;
        await using (var db = await OpenDatabaseAsync())
        {
            member = await TestRows.ShelfBookAsync(db, "Member", null, seriesId, 1);
        }

        foreach (var pick in new[] { archived, member })
        {
            var response = await Client.PostAsJsonAsync("/api/series", new CreateSeriesRequest("Second", [pick]));
            (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        }
    }

    [Test]
    public async Task Delete_WhenSeriesHasBooks_ShouldDetachThemAndCopyTheSeriesKnowledgeBaseDown()
    {
        Guid series, one, two;
        await using (var db = await OpenDatabaseAsync())
        {
            series = await TestRows.SeriesAsync(db, "Doomed");
            one = await TestRows.ShelfBookAsync(db, "One", null, series, 1);
            two = await TestRows.ShelfBookAsync(db, "Two", null, series, 2);
            var spells = await TestRows.CategoryAsync(db, series, "Spells");
            await TestRows.EntryAsync(db, series, spells, "Gravetouch");
            await TestRows.LabelAsync(db, series, "Playtested");
        }

        var impact = (await Client.GetFromJsonAsync<SeriesDeleteImpactDto>($"/api/series/{series}/delete-impact"))!;
        impact.Books.ShouldBe(2);
        impact.Categories.ShouldBe([new SeriesDeleteCategoryDto("Spells", 1)]);
        impact.Labels.ShouldBe(["Playtested"]);

        (await Client.DeleteAsync($"/api/series/{series}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var check = await OpenDatabaseAsync();
        // A soft delete: the series row stays, archived, with no books.
        (await check.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM book WHERE id = @series AND kind = 'series'", new { series })).ShouldBeTrue();
        (await check.ExecuteScalarAsync<long>("SELECT count(*) FROM book WHERE series_id = @series", new { series })).ShouldBe(0);
        foreach (var book in new[] { one, two })
        {
            (await check.ExecuteScalarAsync<Guid?>("SELECT series_id FROM book WHERE id = @book", new { book })).ShouldBeNull();
            (await check.ExecuteScalarAsync<long>(
                """
                SELECT count(*) FROM kb_entry e JOIN kb_category c ON c.id = e.category_id
                WHERE e.book_id = @book AND c.name = 'Spells' AND e.name = 'Gravetouch'
                """, new { book })).ShouldBe(1);
            (await check.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM label WHERE book_id = @book AND name = 'Playtested'", new { book })).ShouldBe(1);
        }
    }

    [Test]
    public async Task Delete_WhenAMemberBookUsesASeriesAsset_ShouldCopyTheAssetDownAndSucceed()
    {
        Guid series, member, asset;
        await using (var db = await OpenDatabaseAsync())
        {
            series = await TestRows.SeriesAsync(db, "Shared art");
            member = await TestRows.ShelfBookAsync(db, "Member", null, series, 1, words: 40);
            asset = await TestRows.AssetAsync(db, series);
            await db.ExecuteAsync(
                """
                INSERT INTO figure (scene_id, asset_id) SELECT id, @asset FROM scene WHERE book_id = @member LIMIT 1;
                UPDATE chapter SET opener_asset_id = @asset WHERE book_id = @member;
                """, new { asset, member });
        }

        (await Client.DeleteAsync($"/api/series/{series}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var check = await OpenDatabaseAsync();
        // The original stays with the archived series; the member's figure and opener now use the copy.
        (await check.ExecuteScalarAsync<long>("SELECT count(*) FROM figure WHERE asset_id = @asset", new { asset })).ShouldBe(0);
        (await check.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM figure f JOIN asset a ON a.id = f.asset_id WHERE a.book_id = @member", new { member })).ShouldBe(1);
    }

    [Test]
    public async Task Delete_WhenUnknown_ShouldAnswer404()
    {
        var response = await Client.DeleteAsync($"/api/series/{Guid.NewGuid()}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
