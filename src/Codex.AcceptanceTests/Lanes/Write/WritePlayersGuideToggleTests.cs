using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Lanes.Write;

// #64 acceptance: two table-heavy Players Guide chapters (mapped fixture) sit in two seeded scenes. Switching the
// scenes Rich -> Source -> Rich several times is only a change of view: no save is sent, and the stored body and
// version come back exactly as they were put.
[TestFixture]
public class WritePlayersGuideToggleTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid TableScene = SeedIds.Scenes.Ch5Sc1;
    private static readonly Guid MoreTablesScene = SeedIds.Scenes.Ch5Sc2;

    private static string Chapter(string file) => File.ReadAllText(
        Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "players-guide", "mapped", file));

    private async Task<ProseDto> GetProseAsync(Guid sceneId)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose"))!;
    }

    private async Task<ProseDto> PutProseAsync(Guid sceneId, string body)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var current = await GetProseAsync(sceneId);
        var response = await client.PutAsJsonAsync($"/api/scenes/{sceneId}/prose", new SaveProseRequest(body, current.Version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await GetProseAsync(sceneId);
    }

    [Test]
    public async Task Toggle_WhenPlayersGuideChaptersSwitchRichSourceRich_ShouldLeaveTheStoredBodiesByteIdentical()
    {
        var chapterThree = await PutProseAsync(TableScene, Chapter("chapter-03.md"));
        var chapterFour = await PutProseAsync(MoreTablesScene, Chapter("chapter-04.md"));
        chapterThree.Body.ShouldBe(Chapter("chapter-03.md"));
        chapterFour.Body.ShouldBe(Chapter("chapter-04.md"));

        var saves = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Method == "PUT" && request.Url.Contains("/prose", StringComparison.Ordinal))
            {
                saves.Add(request.Url);
            }
        };
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(TableScene)).ToBeVisibleAsync();
        await Expect(write.ProseEditor(MoreTablesScene)).ToBeVisibleAsync();

        for (var round = 0; round < 3; round++)
        {
            await write.Scene(TableScene).HoverAsync();
            await write.ModeSource(TableScene).ClickAsync();
            await Expect(write.SourceEditor(TableScene)).ToBeVisibleAsync();
            await Expect(write.SourceEditor(MoreTablesScene)).ToBeVisibleAsync();
            await write.ModeRich(TableScene).ClickAsync();
            await Expect(write.ProseEditor(TableScene)).ToBeVisibleAsync();
            await Expect(write.ProseEditor(MoreTablesScene)).ToBeVisibleAsync();
        }

        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        saves.ShouldBeEmpty();
        var afterThree = await GetProseAsync(TableScene);
        var afterFour = await GetProseAsync(MoreTablesScene);
        afterThree.Body.ShouldBe(chapterThree.Body);
        afterThree.Version.ShouldBe(chapterThree.Version);
        afterFour.Body.ShouldBe(chapterFour.Body);
        afterFour.Version.ShouldBe(chapterFour.Version);
    }
}
