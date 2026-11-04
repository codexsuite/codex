using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;

namespace Codex.WebApi.Tests.Print;

// Effective dpi = pixels across the placed width / placed width in inches, worked out per trim size, column count and
// placement. Page 0.75 in margins, 0.25 in gutter (TrimSizes). The seed's Players Guide figure (480 px) must be flagged.
[TestFixture]
public class AssetDpiTests : AssetApiFixture
{
    private static readonly Guid Figure = SeedIds.Assets.PlayersGuideFigure;

    private Task<AssetDpiDto> DpiAsync(Guid assetId, string query)
        => Client.GetFromJsonAsync<AssetDpiDto>($"/api/assets/{assetId}/dpi?{query}")!;

    [Test, Order(1)]
    public async Task SeedFigure_ShouldBeLowOnEveryTrim()
    {
        // 480 px across a 3.375 in column of a 7 in text block (Letter, two columns).
        var letter = await DpiAsync(Figure, "trim=us-letter&columns=2");
        (letter.EffectiveDpi, letter.PlacedWidthIn, letter.PlacedHeightIn, letter.Status, letter.TargetDpi).ShouldBe((142.2, 3.375, 2.25, "low", 300));

        // One 4.5 in column on 6 x 9; the 4 in text block of a 5.5 x 8.5 page.
        (await DpiAsync(Figure, "trim=6x9")).EffectiveDpi.ShouldBe(106.7);
        (await DpiAsync(Figure, "trim=5.5x8.5&placement=float-top")).EffectiveDpi.ShouldBe(120);
        (await DpiAsync(Figure, "trim=a4&columns=3")).Status.ShouldBe("low");
    }

    [Test, Order(2)]
    public async Task Dpi_ShouldDifferPerTrimAndPlacement()
    {
        var asset = await UploadOkAsync(SeedIds.Books.PlayersGuide, "plate.png", TestImages.Photo(2400, 1600), "image/png");

        var sixByNine = await DpiAsync(asset.Id, "trim=6x9");
        var letterColumns = await DpiAsync(asset.Id, "trim=us-letter&columns=2");
        var letterPage = await DpiAsync(asset.Id, "trim=us-letter&placement=page");
        var letterSpread = await DpiAsync(asset.Id, "trim=us-letter&placement=spread");
        var letterFloat = await DpiAsync(asset.Id, "trim=us-letter&placement=float-top");

        sixByNine.EffectiveDpi.ShouldBe(533.3);
        sixByNine.Status.ShouldBe("ok");
        letterColumns.EffectiveDpi.ShouldBe(711.1);
        // page is the float page inside the 7 in text block (figure[p]), not the full 8.5 in trim; spread is two of them.
        (letterPage.EffectiveDpi, letterPage.PlacedWidthIn, letterPage.Status).ShouldBe((342.9, 7, "ok"));
        letterFloat.EffectiveDpi.ShouldBe(342.9);
        (letterSpread.EffectiveDpi, letterSpread.Status).ShouldBe((171.4, "low"));
        (await DpiAsync(asset.Id, "trim=us-letter&placement=spread&target=150")).Status.ShouldBe("ok");
    }

    [Test, Order(2)]
    public async Task Dpi_OfATallImage_ShouldFitTheBoxHeight()
    {
        // 1000 x 3000 on a 6 x 9 page: the 7.5 in text height limits it to 2.5 in wide, so 400 dpi, not 222 across 4.5 in.
        var tall = await UploadOkAsync(SeedIds.Books.PlayersGuide, "scroll.png", TestImages.Photo(1000, 3000), "image/png");

        var fit = await DpiAsync(tall.Id, "trim=6x9&placement=page");

        (fit.PlacedWidthIn, fit.PlacedHeightIn, fit.EffectiveDpi, fit.Status).ShouldBe((2.5, 7.5, 400, "ok"));
    }

    [Test, Order(3)]
    public async Task Info_WithATrim_ShouldCarryTheDpi()
    {
        var info = (await Client.GetFromJsonAsync<AssetDto>($"/api/assets/{Figure}/info?trim=us-letter&columns=2"))!;
        var list = (await Client.GetFromJsonAsync<List<AssetDto>>($"/api/books/{SeedIds.Books.PlayersGuide}/assets?trim=6x9"))!;

        info.Dpi.ShouldNotBeNull().EffectiveDpi.ShouldBe(142.2);
        list.Single(a => a.Id == Figure).Dpi.ShouldNotBeNull().EffectiveDpi.ShouldBe(106.7);
        list.ShouldAllBe(a => a.Dpi != null);
    }

    [Test, Order(4)]
    public async Task Dpi_WithAnUnknownTrim_ShouldBe400()
    {
        using var response = await Client.GetAsync($"/api/assets/{Figure}/dpi?trim=tabloid");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(5)]
    public async Task Dpi_WithoutATrim_ShouldBe400()
    {
        using var response = await Client.GetAsync($"/api/assets/{Figure}/dpi");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [TestCase("trim=6x9&placement=banner")]
    [TestCase("trim=6x9&columns=4")]
    [TestCase("trim=6x9&target=200")]
    [Order(6)]
    public async Task Dpi_WithABadOption_ShouldBe400(string query)
    {
        using var response = await Client.GetAsync($"/api/assets/{Figure}/dpi?{query}");

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(7)]
    public async Task Dpi_OfAnUnknownAsset_ShouldBe404()
    {
        using var response = await Client.GetAsync($"/api/assets/{Guid.NewGuid()}/dpi?trim=6x9");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
