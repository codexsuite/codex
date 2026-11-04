using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.Contracts.Structure;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Print;

// Act and chapter openers (#65): set with a caption, cleared (which drops the caption), refused for an image the
// book cannot use, and carried by the outline Plan and Write draw.
[TestFixture]
public class OpenerCommandTests : ApiFixture
{
    private static readonly Guid Asset = SeedIds.Assets.PlayersGuideFigure;

    private sealed record OpenerRow(Guid? AssetId, string? Caption);

    [Test, Order(1)]
    public async Task SetAct_WhenTheImageIsTheBooks_ShouldStoreItAndTheCaption()
    {
        using var response = await Client.PutAsJsonAsync($"/api/acts/{SeedIds.Acts.PlayersGuideAct1}/opener", new SetOpenerRequest(Asset, " Part one "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<OpenerDto>()).ShouldBe(new OpenerDto(SeedIds.Acts.PlayersGuideAct1, "act", Asset, "Part one"));
        (await RowAsync("act", SeedIds.Acts.PlayersGuideAct1)).ShouldBe(new OpenerRow(Asset, "Part one"));
    }

    [Test, Order(2)]
    public async Task SetAct_WhenTheAssetIsNull_ShouldClearTheOpenerAndItsCaption()
    {
        using var response = await Client.PutAsJsonAsync($"/api/acts/{SeedIds.Acts.PlayersGuideAct1}/opener", new SetOpenerRequest(null, "ignored"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RowAsync("act", SeedIds.Acts.PlayersGuideAct1)).ShouldBe(new OpenerRow(null, null));
    }

    [Test, Order(3)]
    public async Task SetChapter_WhenTheImageIsTheBooks_ShouldStoreIt()
    {
        using var response = await Client.PutAsJsonAsync($"/api/chapters/{SeedIds.Chapters.PlayersGuideCh1}/opener", new SetOpenerRequest(Asset));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RowAsync("chapter", SeedIds.Chapters.PlayersGuideCh1)).ShouldBe(new OpenerRow(Asset, null));
    }

    [Test, Order(4)]
    public async Task SetChapter_WhenTheImageIsAnotherBooks_ShouldBeValidation()
    {
        using var response = await Client.PutAsJsonAsync($"/api/chapters/{SeedIds.Chapters.Ch3}/opener", new SetOpenerRequest(Asset));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await RowAsync("chapter", SeedIds.Chapters.Ch3)).ShouldBe(new OpenerRow(null, null));
    }

    [Test, Order(5)]
    public async Task SetAct_WhenTheImageIsUnknown_ShouldBeNotFound()
    {
        using var response = await Client.PutAsJsonAsync($"/api/acts/{SeedIds.Acts.PlayersGuideAct1}/opener", new SetOpenerRequest(Guid.NewGuid()));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(6)]
    public async Task Outline_WhenAChapterHasAnOpener_ShouldCarryItAndItsThumbnailOnceRendered()
    {
        (await ChapterOpenerAsync()).ShouldBe(new OutlineOpenerDto(Asset, null, null));

        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync(
                "INSERT INTO asset_rendition (asset_id, purpose, mime, width_px, height_px, bytes, storage_key) VALUES (@Asset, 'web', 'image/webp', 480, 320, 10, 'test/web.webp')",
                new { Asset });
        }

        (await ChapterOpenerAsync()).ShouldBe(new OutlineOpenerDto(Asset, null, $"/api/assets/{Asset}/renditions/web"));
    }

    private async Task<OutlineOpenerDto?> ChapterOpenerAsync()
    {
        var outline = await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{SeedIds.Books.PlayersGuide}/outline");
        var act = outline!.Acts.Single(a => a.Id == SeedIds.Acts.PlayersGuideAct1);
        act.Opener.ShouldBeNull();
        return act.Chapters.Single(c => c.Id == SeedIds.Chapters.PlayersGuideCh1).Opener;
    }

    private async Task<OpenerRow> RowAsync(string table, Guid id)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.QuerySingleAsync<OpenerRow>(
            table == "act"
                ? "SELECT opener_asset_id AS AssetId, opener_caption AS Caption FROM act WHERE id = @id"
                : "SELECT opener_asset_id AS AssetId, opener_caption AS Caption FROM chapter WHERE id = @id",
            new { id });
    }
}
