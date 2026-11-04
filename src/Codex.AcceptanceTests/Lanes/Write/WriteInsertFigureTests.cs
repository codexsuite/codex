using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Images;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using SkiaSharp;

namespace Codex.AcceptanceTests.Lanes.Write;

// `/` → Figure… with a freshly uploaded image: the upload is selected, Insert figure writes the row and the line, the
// card shows the caption and (once the rendition worker has made it) the image; then the card's Remove takes it out.
[TestFixture]
public class WriteInsertFigureTests : WriteLaneTest
{
    private static readonly Guid Book = SeedIds.Books.ProductEngineering;
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc1;

    // A 64 x 48 PNG the rendition worker can decode.
    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Rgba8888, SKAlphaType.Opaque));
        bitmap.Erase(new SKColor(90, 120, 160));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static Guid _figureId;

    private async Task<T> GetAsync<T>(string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<T>(path))!;
    }

    [Test, Order(1)]
    public async Task InsertFigure_WithAnUploadedImage_ShouldPlaceItAndShowTheCard()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(Book.ToString()));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();

        await write.PlaceCaretAtStartAsync(Scene);
        await Page.Keyboard.TypeAsync("/fig");
        await write.SlashItem(WriteTestIds.SlashFigure).ClickAsync();
        await Expect(Page.GetByTestId(WriteTestIds.FigureDialog)).ToBeVisibleAsync();

        var uploaded = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/assets", "POST", () =>
            Page.GetByTestId(ImagesTestIds.UploadInput).SetInputFilesAsync(new FilePayload
            {
                Name = "map.png", MimeType = "image/png", Buffer = Png(),
            }));
        uploaded.Status.ShouldBe((int)HttpStatusCode.Created);
        var asset = JsonSerializer.Deserialize<AssetDto>(await uploaded.TextAsync(), Web)!;
        await Expect(Page.GetByTestId(ImagesTestIds.Option(asset.Id))).ToHaveAttributeAsync("aria-checked", "true");

        await Page.GetByTestId(WriteTestIds.FigureCaptionInput).FillAsync("The yard at dawn.");
        await Page.GetByTestId(WriteTestIds.FigureAltInput).FillAsync("An empty yard.");
        var created = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/figures", "POST",
            () => Page.GetByTestId(WriteTestIds.FigureDialogSubmit).ClickAsync());

        created.Status.ShouldBe((int)HttpStatusCode.OK);
        _figureId = JsonSerializer.Deserialize<CreateFigureResult>(await created.TextAsync(), Web)!.FigureId;
        await Expect(Page.GetByTestId(WriteTestIds.FigureCaption(_figureId))).ToHaveTextAsync("The yard at dawn.");
        // The worker makes the web rendition in the background; the card polls until it can draw it.
        await Expect(Page.GetByTestId(WriteTestIds.FigureImage(_figureId))).ToHaveJSPropertyAsync("naturalWidth", 64, new() { Timeout = 30_000 });

        var figure = await GetAsync<FigureDto>($"/api/figures/{_figureId}");
        (figure.AssetId, figure.Caption, figure.AltText, figure.Placement).ShouldBe((asset.Id, "The yard at dawn.", "An empty yard.", "column"));
        (await GetAsync<ProseDto>($"/api/scenes/{Scene}/prose")).Body.ShouldContain($"::figure{{id=\"{_figureId}\"}}");
    }

    [Test, Order(2)]
    public async Task RemoveFigure_FromTheCard_ShouldTakeItsLineOut()
    {
        await Page.GotoAsync(Links.Write(Book.ToString()));
        await Expect(Page.GetByTestId(WriteTestIds.Figure(_figureId))).ToBeVisibleAsync();

        var removed = await Page.RunAndWaitForApiAsync($"/api/figures/{_figureId}/remove", "POST",
            () => Page.GetByTestId(WriteTestIds.FigureRemove(_figureId)).ClickAsync());

        removed.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(Page.GetByTestId(WriteTestIds.Figure(_figureId))).ToHaveCountAsync(0);
        (await GetAsync<ProseDto>($"/api/scenes/{Scene}/prose")).Body.ShouldNotContain($"::figure{{id=\"{_figureId}\"}}");
    }
}
