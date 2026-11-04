using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Print;
using Codex.Contracts.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Lanes.Write;

// The Players Guide's low-dpi `::figure` (D3) drawn as a card in Write, its missing-asset state, and Format → Preview in
// Layout. The seed's asset row has no file behind it, so the first scenario gives it a web rendition (a 1 x 1 PNG; the
// dpi comes from the row's 480 x 320 pixels, not from the file).
[TestFixture]
public class WriteFigureTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.PlayersGuide.ToString();
    private static readonly Guid Figure = SeedIds.Figures.PlayersGuideFigure;
    private static readonly Guid Asset = SeedIds.Assets.PlayersGuideFigure;
    private static readonly Guid Scene = SeedIds.Scenes.PlayersGuideFigureScene;

    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAHnOcQAAAAABJRU5ErkJggg==";

    private async Task GiveTheAssetAWebRenditionAsync()
    {
        var key = $"seed/players-guide/{Asset}-web.png";
        var blobs = Host.Services.GetRequiredService<IBlobStore>();
        await blobs.PutAsync(key, new MemoryStream(Convert.FromBase64String(OnePixelPng)), "image/png", CancellationToken.None);
        await ExecuteSqlAsync(
            "INSERT INTO asset_rendition (asset_id, purpose, mime, width_px, height_px, bytes, storage_key) VALUES (@asset, 'web', 'image/png', 1, 1, 70, @key)",
            ("@asset", Asset), ("@key", key));
    }

    [Test, Order(1)]
    public async Task FigureCard_WhenTheAssetIsLowResolution_ShouldShowImageCaptionAndBadge()
    {
        await GiveTheAssetAWebRenditionAsync();
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var info = (await client.GetFromJsonAsync<FigureDto>($"/api/figures/{Figure}"))!;
        info.Missing.ShouldBeFalse();
        info.Dpi!.Status.ShouldBe("low");

        await Page.GotoAsync(Links.Write(BookId));

        await Expect(Page.GetByTestId(WriteTestIds.FigureImage(Figure))).ToHaveJSPropertyAsync("naturalWidth", 1);
        await Expect(Page.GetByTestId(WriteTestIds.FigureCaption(Figure))).ToHaveTextAsync("The Fallen World, as the players first see it.");
        await Expect(Page.GetByTestId(WriteTestIds.FigureLowDpi(Figure))).ToContainTextAsync($"{Math.Round(info.Dpi.EffectiveDpi)} dpi");

        // The card never rewrites the line.
        var prose = (await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose"))!;
        prose.Body.ShouldContain($"::figure{{id=\"{Figure}\"}}");
    }

    [Test, Order(2)]
    public async Task FigureCard_WhenTheAssetIsGone_ShouldShowTheMissingState()
    {
        await ExecuteSqlAsync("UPDATE asset SET archived_at = now() WHERE id = @asset", ("@asset", Asset));

        await Page.GotoAsync(Links.Write(BookId));

        await Expect(Page.GetByTestId(WriteTestIds.FigureMissing(Figure))).ToContainTextAsync("missing");
        await Expect(Page.GetByTestId(WriteTestIds.FigureImage(Figure))).ToHaveCountAsync(0);
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        (await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose"))!.Body.ShouldContain($"::figure{{id=\"{Figure}\"}}");
    }

    [Test, Order(3)]
    public async Task FormatMenu_WhenOpened_ShouldLinkToTheBooksLayoutTab()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));

        await write.FormatButton.ClickAsync();

        await Expect(Page.GetByTestId(WriteTestIds.FormatPreview)).ToHaveAttributeAsync("href", Links.Layout(BookId));
    }
}
