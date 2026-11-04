using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.Contracts.Settings;
using Dapper;

namespace Codex.WebApi.Tests.Settings;

// PUT/DELETE /api/books/{id}/cover and the CoverUrl the book header and Library rows carry.
[TestFixture]
public class BookCoverEndpointTests : SchemaApiFixture
{
    private async Task<Guid> BookAsync(string title, Guid? series = null)
    {
        await using var db = await OpenDatabaseAsync();
        return await TestRows.BookAsync(db, title, series);
    }

    private async Task<Guid> AssetAsync(Guid bookId, string kind = "image", bool archived = false)
    {
        await using var db = await OpenDatabaseAsync();
        return await TestRows.AssetAsync(db, bookId, kind, archived);
    }

    private Task<HttpResponseMessage> PutCoverAsync(Guid bookId, Guid? assetId)
        => Client.PutAsJsonAsync($"/api/books/{bookId}/cover", new SetBookCoverRequest(assetId));

    private async Task<string?> HeaderCoverAsync(Guid bookId)
        => (await Client.GetFromJsonAsync<BookHeaderDto>($"/api/books/{bookId}"))!.CoverUrl;

    [Test]
    public async Task Put_WhenBookImage_ShouldShowTheOriginalUntilTheWebRenditionIsMade()
    {
        var book = await BookAsync("Covered");
        var asset = await AssetAsync(book);

        (await PutCoverAsync(book, asset)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await HeaderCoverAsync(book)).ShouldBe($"/api/assets/{asset}");
        var library = (await Client.GetFromJsonAsync<LibraryDto>("/api/library"))!;
        library.Standalone.Single(b => b.Id == book).CoverUrl.ShouldBe($"/api/assets/{asset}");

        await using (var db = await OpenDatabaseAsync())
        {
            await TestRows.WebRenditionAsync(db, asset);
        }
        (await HeaderCoverAsync(book)).ShouldBe($"/api/assets/{asset}/renditions/web");
    }

    [Test]
    public async Task Put_WhenSeriesImage_ShouldBecomeTheCover()
    {
        Guid series;
        await using (var db = await OpenDatabaseAsync())
        {
            series = await TestRows.SeriesAsync(db, "Cover Series");
        }
        var book = await BookAsync("Series book", series);
        var asset = await AssetAsync(series);

        (await PutCoverAsync(book, asset)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await HeaderCoverAsync(book)).ShouldBe($"/api/assets/{asset}");
        var library = (await Client.GetFromJsonAsync<LibraryDto>("/api/library"))!;
        library.Series.Single(s => s.Id == series).Books.Single().CoverUrl.ShouldBe($"/api/assets/{asset}");
    }

    [Test]
    public async Task Delete_WhenCovered_ShouldFallBackToTheGeneratedCover()
    {
        var book = await BookAsync("Uncovered");
        (await PutCoverAsync(book, await AssetAsync(book))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Client.DeleteAsync($"/api/books/{book}/cover")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var header = (await Client.GetFromJsonAsync<BookHeaderDto>($"/api/books/{book}"))!;
        header.CoverUrl.ShouldBeNull();
        header.CoverPalette.ShouldStartWith("cover-");
        (await Client.DeleteAsync($"/api/books/{book}/cover")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Put_WhenNotAnImageOfTheBook_ShouldAnswer400AndKeepTheCover()
    {
        var book = await BookAsync("Strict");
        var cover = await AssetAsync(book);
        (await PutCoverAsync(book, cover)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var otherBooks = await AssetAsync(await BookAsync("Someone else"));
        var document = await AssetAsync(book, "document");

        foreach (var assetId in new Guid?[] { null, otherBooks, document })
        {
            var response = await PutCoverAsync(book, assetId);
            (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        }

        (await HeaderCoverAsync(book)).ShouldBe($"/api/assets/{cover}");
    }

    [Test]
    public async Task Put_WhenAssetUnknownOrArchived_ShouldAnswer404()
    {
        var book = await BookAsync("Missing art");
        var archived = await AssetAsync(book, archived: true);

        foreach (var assetId in new[] { Guid.NewGuid(), archived })
        {
            var response = await PutCoverAsync(book, assetId);
            (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        }

        (await HeaderCoverAsync(book)).ShouldBeNull();
    }

    [Test]
    public async Task Cover_WhenBookUnknownOrASeries_ShouldAnswer404()
    {
        Guid series;
        await using (var db = await OpenDatabaseAsync())
        {
            series = await TestRows.SeriesAsync(db, "Not a book");
        }
        var asset = await AssetAsync(series);

        foreach (var bookId in new[] { Guid.NewGuid(), series })
        {
            (await (await PutCoverAsync(bookId, asset)).ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
            (await (await Client.DeleteAsync($"/api/books/{bookId}/cover")).ReadApiErrorAsync(HttpStatusCode.NotFound))
                .Code.ShouldBe(ApiErrorCodes.NotFound);
        }
    }

    [Test]
    public async Task Get_WhenTheCoverIsArchived_ShouldShowTheGeneratedCover()
    {
        var book = await BookAsync("Faded");
        var asset = await AssetAsync(book);
        (await PutCoverAsync(book, asset)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync("UPDATE asset SET archived_at = now() WHERE id = @asset", new { asset });
        }

        (await HeaderCoverAsync(book)).ShouldBeNull();
    }
}
