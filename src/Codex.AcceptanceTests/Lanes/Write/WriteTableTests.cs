using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Write;

// A table made from the slash menu: add a column from the table toolbar, type in a cell, and it is saved as a pipe
// table that is still there after a reload.
[TestFixture]
public class WriteTableTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid SceneId = SeedIds.Scenes.Ch5Sc1;
    private static readonly string ProsePath = $"/api/scenes/{SceneId}/prose";

    private async Task<ProseDto> GetProseAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>(ProsePath))!;
    }

    [Test, Order(1)]
    public async Task Table_WhenInsertedFromSlashMenuAndEdited_ShouldSurviveAReload()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(SceneId)).ToBeVisibleAsync();
        var tables = write.Tables(SceneId);
        var headers = write.TableHeaderCells(SceneId);
        await Expect(tables).ToHaveCountAsync(0);

        await write.ProseEditor(SceneId).ClickAsync();
        await Page.Keyboard.PressAsync("Control+Home");
        await Page.Keyboard.TypeAsync("/Tab");
        await write.SlashItem(WriteTestIds.SlashTable).ClickAsync();
        await Expect(tables).ToHaveCountAsync(1);
        await Expect(headers).ToHaveCountAsync(2);

        await write.TableAddColumnAfter.ClickAsync();
        await Expect(headers).ToHaveCountAsync(3);

        var response = await Page.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await Page.Keyboard.TypeAsync("Name");
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        (await GetProseAsync()).Body.ShouldContain("| Name |");

        await Page.ReloadAsync();

        await Expect(write.ProseEditor(SceneId)).ToBeVisibleAsync();
        await Expect(tables).ToHaveCountAsync(1);
        await Expect(headers).ToHaveCountAsync(3);
        await Expect(headers.First).ToHaveTextAsync("Name");
    }
}
