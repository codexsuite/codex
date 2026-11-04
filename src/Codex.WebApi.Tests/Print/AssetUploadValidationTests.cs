using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.Print;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Dapper;

namespace Codex.WebApi.Tests.Print;

// Uploads that must be refused, and that nothing is left behind when they are.
[TestFixture]
public class AssetUploadValidationTests : AssetApiFixture
{
    private static readonly Guid BookId = SeedIds.Books.PlayersGuide;

    [Test, Order(1)]
    public async Task Upload_OfAnUnsupportedType_ShouldBe400()
    {
        using var response = await UploadAsync(BookId, "notes.txt", "hello"u8.ToArray(), "text/plain");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(2)]
    public async Task Upload_OfAGif_ShouldBe400()
    {
        using var response = await UploadAsync(BookId, "spin.gif", "GIF89a"u8.ToArray(), "image/gif");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(3)]
    public async Task Upload_OfBytesThatAreNotAnImage_ShouldBe400()
    {
        using var response = await UploadAsync(BookId, "fake.png", "not really a png"u8.ToArray(), "image/png");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(4)]
    public async Task Upload_OverTheSizeCap_ShouldBe400()
    {
        using var response = await UploadAsync(BookId, "huge.png", new byte[AssetService.MaxUploadBytes + 1], "image/png");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(4)]
    public async Task Upload_OverTheMegapixelCap_ShouldBe400()
    {
        var blank = TestImages.BlankPng(8001, 8000); // 64.008 MP, a valid PNG of a few KB
        blank.Length.ShouldBeLessThan(1024 * 1024);

        using var response = await UploadAsync(BookId, "wall.png", blank, "image/png");

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        (error.Code, error.Error).ShouldBe((ApiErrorCodes.Validation, "Images can be at most 64 megapixels."));
    }

    [Test, Order(5)]
    public async Task Upload_WithoutAFile_ShouldBe400()
    {
        using var response = await UploadAsync(BookId, null, null, "image/png", source: "a note");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(6)]
    public async Task Upload_WithAnEmptyFile_ShouldBe400()
    {
        using var response = await UploadAsync(BookId, "empty.png", [], "image/png");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(7)]
    public async Task Upload_AsJson_ShouldBe400()
    {
        using var response = await Client.PostAsJsonAsync($"/api/books/{BookId}/assets", new { file = "x" });

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(8)]
    public async Task Upload_ToAnUnknownBook_ShouldBe404()
    {
        using var response = await UploadAsync(Guid.NewGuid(), "map.png", TestImages.Photo(40, 30), "image/png");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(9)]
    public async Task List_OfAnUnknownBook_ShouldBe404()
    {
        using var response = await Client.GetAsync($"/api/books/{Guid.NewGuid()}/assets");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(10)]
    public async Task Original_OfAnUnknownAsset_ShouldBe404()
    {
        using var response = await Client.GetAsync($"/api/assets/{Guid.NewGuid()}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(11)]
    public async Task RefusedUploads_ShouldLeaveNoAssetRows()
    {
        await using var connection = await OpenDatabaseAsync();

        var assets = await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM asset WHERE book_id = @id", new { id = BookId });
        var jobs = await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM asset_rendition_job");

        (assets, jobs).ShouldBe((1, 0)); // just the seed figure
        Directory.EnumerateFiles(Host.BlobRoot, "*", SearchOption.AllDirectories).ShouldBeEmpty(); // and no stray blob
    }

    [Test, Order(12)]
    public async Task Upload_ToASeries_ShouldBeSharedWithItsBooks()
    {
        var seriesId = await SeriesIdAsync();
        var shared = await UploadOkAsync(seriesId, "emblem.png", TestImages.Photo(200, 100), "image/png");

        var inBook = (await Client.GetFromJsonAsync<List<AssetDto>>($"/api/books/{BookId}/assets"))!;

        shared.Scope.ShouldBe("series");
        inBook.ShouldContain(a => a.Id == shared.Id);
    }

    [Test, Order(13)]
    public async Task Upload_Notes_ShouldBeKeptInMeta()
    {
        using var response = await UploadAsync(BookId, "noted.png", TestImages.Photo(60, 40), "image/png", source: " Scanned from the 1987 sketchbook ");
        var asset = (await response.Content.ReadFromJsonAsync<AssetDto>())!;
        await using var connection = await OpenDatabaseAsync();

        var meta = await connection.ExecuteScalarAsync<string>("SELECT meta ->> 'source' FROM asset WHERE id = @id", new { id = asset.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        meta.ShouldBe("Scanned from the 1987 sketchbook");
    }

    private async Task<Guid> SeriesIdAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT series_id FROM book WHERE id = @id", new { id = BookId });
    }
}
