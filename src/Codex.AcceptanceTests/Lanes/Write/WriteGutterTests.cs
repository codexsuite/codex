using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Structure;

namespace Codex.AcceptanceTests.Lanes.Write;

// The scene gutter's Label picker.
[TestFixture]
public class WriteGutterTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc1;

    [Test, Order(1)]
    public async Task Label_WhenChanged_ShouldSave()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.SceneLabels(Scene)).ToHaveCountAsync(1);

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/labels", "PUT", async () =>
        {
            await write.SceneLabelAdd(Scene).ClickAsync();
            await write.LabelOption(SeedIds.Labels.ReadyToWrite).ClickAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SceneLabels(Scene)).ToHaveCountAsync(2);
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var outline = await client.GetFromJsonAsync<OutlineDto>($"/api/books/{BookId}/outline");
        var labels = outline!.Acts.SelectMany(a => a.Chapters).SelectMany(c => c.Scenes).Single(s => s.Id == Scene).Labels;
        labels.Select(l => l.Id).ShouldBe([SeedIds.Labels.Drafting, SeedIds.Labels.ReadyToWrite], ignoreOrder: true);
    }
}
