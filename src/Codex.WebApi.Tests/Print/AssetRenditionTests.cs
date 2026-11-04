using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.Contracts.Storage;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Codex.WebApi.Tests.Print;

// An upload yields three renditions (web, proxy, print) made by the background worker, not the request.
[TestFixture]
public class AssetRenditionTests : AssetApiFixture
{
    private static readonly Guid BookId = SeedIds.Books.PlayersGuide;
    private readonly byte[] _photo = TestImages.Photo(3200, 2000);
    private AssetDto _uploaded = null!;
    private AssetDto _ready = null!;

    [Test, Order(1)]
    public async Task Upload_ShouldQueueTheRenditionsAndKeepTheOriginal()
    {
        _uploaded = await UploadOkAsync(BookId, @"C:\art\Cover Map.png", _photo, "image/png");

        (_uploaded.Status, _uploaded.Scope, _uploaded.Kind, _uploaded.Mime).ShouldBe(("processing", "book", "image", "image/png"));
        (_uploaded.BookId, _uploaded.Filename, _uploaded.WidthPx, _uploaded.HeightPx).ShouldBe((BookId, "Cover Map.png", 3200, 2000));
        _uploaded.Bytes.ShouldBe(_photo.Length);
        _uploaded.Renditions.ShouldBeEmpty();
        _uploaded.CreatedLabel.ShouldBe("just now");
        using var original = await Client.GetAsync(_uploaded.Url);
        (await original.Content.ReadAsByteArrayAsync()).ShouldBe(_photo);
    }

    [Test, Order(2)]
    public async Task Worker_ShouldMakeWebProxyAndPrintRenditions()
    {
        _ready = await SettledAsync(_uploaded.Id);

        _ready.Status.ShouldBe("ready");
        _ready.Error.ShouldBeNull();
        _ready.Renditions.Select(r => (r.Purpose, r.Mime)).ShouldBe(
            [("web", "image/webp"), ("proxy", "image/jpeg"), ("print", "image/jpeg")]);
        var sizes = _ready.Renditions.ToDictionary(r => r.Purpose);
        (sizes["web"].WidthPx, sizes["web"].HeightPx).ShouldBe((1600, 1000));
        (sizes["proxy"].WidthPx, sizes["proxy"].HeightPx).ShouldBe((2625, 1641)); // 150 dpi across a 17.5 in spread
        (sizes["print"].WidthPx, sizes["print"].HeightPx).ShouldBe((3200, 2000)); // below 300 dpi * 17.5 in: never upscaled
        _ready.Renditions.ShouldAllBe(r => r.Bytes > 0);
    }

    [Test, Order(3)]
    public async Task Renditions_ShouldDownloadAsImagesOfTheStatedSize()
    {
        foreach (var rendition in _ready.Renditions)
        {
            using var response = await Client.GetAsync(rendition.Url);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe(rendition.Mime);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            bytes.Length.ShouldBe((int)rendition.Bytes);
            using var decoded = TestImages.Decode(bytes);
            (decoded.Width, decoded.Height).ShouldBe((rendition.WidthPx, rendition.HeightPx));
        }
    }

    [Test, Order(4)]
    public async Task List_ShouldIncludeTheAssetWithItsRenditionsAndTheSeedFigure()
    {
        var assets = (await Client.GetFromJsonAsync<List<AssetDto>>($"/api/books/{BookId}/assets"))!;

        assets.Select(a => a.Id).ShouldBe([_uploaded.Id, SeedIds.Assets.PlayersGuideFigure]);
        assets[0].Renditions.Count.ShouldBe(3);
        assets[1].Status.ShouldBe("none");
        assets[1].Dpi.ShouldBeNull();
    }

    [Test, Order(5)]
    public async Task Requeue_ShouldRemakeTheRenditions()
    {
        var before = await RenditionKeysAsync(_uploaded.Id);
        using var response = await Client.PostAsync($"/api/assets/{_uploaded.Id}/renditions", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var again = await SettledAsync(_uploaded.Id);
        var after = await RenditionKeysAsync(_uploaded.Id);
        var blobs = Host.Services.GetRequiredService<IBlobStore>();

        again.Status.ShouldBe("ready");
        again.Renditions.Count.ShouldBe(3);
        after.Keys.ShouldBe(before.Keys, ignoreOrder: true); // same purposes ...
        foreach (var purpose in before.Keys)
        {
            after[purpose].ShouldNotBe(before[purpose]); // ... each in a newly written file
            (await blobs.ExistsAsync(after[purpose], CancellationToken.None)).ShouldBeTrue();
            (await blobs.ExistsAsync(before[purpose], CancellationToken.None)).ShouldBeFalse();
        }
    }

    [Test, Order(6)]
    public async Task LineArtPng_ShouldKeepAPngPrintRendition()
    {
        var art = await UploadOkAsync(BookId, "rune.png", TestImages.LineArt(800, 600), "image/png");
        var ready = await SettledAsync(art.Id);

        ready.Renditions.Single(r => r.Purpose == "print").Mime.ShouldBe("image/png");
        ready.Renditions.Single(r => r.Purpose == "web").Mime.ShouldBe("image/webp");
    }

    [Test, Order(7)]
    public async Task WebpUpload_ShouldBeAccepted()
    {
        var webp = await UploadOkAsync(BookId, "sketch.webp", TestImages.Photo(400, 300, SKEncodedImageFormat.Webp), "image/webp");
        var ready = await SettledAsync(webp.Id);

        (webp.Mime, webp.WidthPx).ShouldBe(("image/webp", 400));
        ready.Renditions.Count.ShouldBe(3);
    }

    [Test, Order(8)]
    public async Task UploadOfJpegDeclaredAsPng_ShouldBeStoredAsWhatItIs()
    {
        var asset = await UploadOkAsync(BookId, "photo.png", TestImages.Photo(300, 200, SKEncodedImageFormat.Jpeg), "image/png");

        asset.Mime.ShouldBe("image/jpeg");
        (await SettledAsync(asset.Id)).Status.ShouldBe("ready");
    }

    [Test, Order(9)]
    public async Task Requeue_OfTheSeedFigure_ShouldFailBecauseItsFileIsMissing()
    {
        using var response = await Client.PostAsync($"/api/assets/{SeedIds.Assets.PlayersGuideFigure}/renditions", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var failed = await SettledAsync(SeedIds.Assets.PlayersGuideFigure);

        failed.Status.ShouldBe("failed");
        failed.Error.ShouldBe("The original file is missing from storage.");
        failed.Renditions.ShouldBeEmpty();
    }

    [Test, Order(10)]
    public async Task Rendition_BeforeItExists_ShouldBe404()
    {
        using var response = await Client.GetAsync($"/api/assets/{SeedIds.Assets.PlayersGuideFigure}/renditions/web");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(11)]
    public async Task Rendition_OfAnUnknownPurpose_ShouldBe404()
    {
        using var response = await Client.GetAsync($"/api/assets/{_uploaded.Id}/renditions/huge");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(12)]
    public async Task Requeue_OfAnUnknownAsset_ShouldBe404()
    {
        using var response = await Client.PostAsync($"/api/assets/{Guid.NewGuid()}/renditions", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(13)]
    public async Task Requeue_WhileTheWorkerIsRunning_ShouldBe409()
    {
        await using (var connection = await OpenDatabaseAsync())
        {
            await connection.ExecuteAsync(
                "UPDATE asset_rendition_job SET status = 'running', started_at = now() WHERE asset_id = @id", new { id = _uploaded.Id });
        }

        using var response = await Client.PostAsync($"/api/assets/{_uploaded.Id}/renditions", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe("conflict");
    }

    private async Task<Dictionary<string, string>> RenditionKeysAsync(Guid assetId)
    {
        await using var connection = await OpenDatabaseAsync();
        return (await connection.QueryAsync<(string Purpose, string Key)>(
            "SELECT purpose, storage_key FROM asset_rendition WHERE asset_id = @assetId", new { assetId })).ToDictionary(r => r.Purpose, r => r.Key);
    }
}
