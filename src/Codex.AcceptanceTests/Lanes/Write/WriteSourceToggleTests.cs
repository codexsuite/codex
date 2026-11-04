using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Write;

// The Rich / Source switch on a scene: an edit typed in Source saves through the same PUT and survives a reload (the
// choice is remembered too), and a Source edit that invents a beat is refused by the server.
[TestFixture]
public class WriteSourceToggleTests : WriteLaneTest
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
    public async Task Source_WhenEditedAndSaved_ShouldSurviveAReloadInSource()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(SceneId)).ToBeVisibleAsync();
        var before = await GetProseAsync();

        await write.Scene(SceneId).HoverAsync();
        await write.ModeSource(SceneId).ClickAsync();
        await Expect(write.SourceEditor(SceneId)).ToBeVisibleAsync();
        await Expect(write.ProseEditor(SceneId)).ToHaveCountAsync(0);

        var response = await Page.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await write.SourceEditor(SceneId).ClickAsync();
            await Page.Keyboard.PressAsync("Control+Home");
            await Page.Keyboard.TypeAsync("Typed in Source. ");
            await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "unsaved");
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        var after = await GetProseAsync();
        after.Version.ShouldBe(before.Version + 1);
        after.Body.ShouldStartWith("Typed in Source. ");

        await Page.ReloadAsync();

        await Expect(write.SourceEditor(SceneId)).ToBeVisibleAsync();
        await Expect(write.SourceEditor(SceneId)).ToContainTextAsync("Typed in Source.");
        await write.ModeRich(SceneId).ClickAsync();
        await Expect(write.ProseEditor(SceneId)).ToContainTextAsync("Typed in Source.");
        (await GetProseAsync()).Version.ShouldBe(after.Version);
    }

    [Test, Order(2)]
    public async Task Source_WhenItInventsABeat_ShouldBeRefusedAndLeaveTheProseAlone()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(SceneId)).ToBeVisibleAsync();
        await write.Scene(SceneId).HoverAsync();
        await write.ModeSource(SceneId).ClickAsync();
        await Expect(write.SourceEditor(SceneId)).ToBeVisibleAsync();
        var before = await GetProseAsync();

        var response = await Page.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await write.SourceEditor(SceneId).ClickAsync();
            await Page.Keyboard.PressAsync("Control+End");
            await Page.Keyboard.TypeAsync($"\n\n::beat{{id=\"{Guid.NewGuid()}\"}}\n");
            await write.Manuscript.FocusAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.BadRequest);
        (await response.TextAsync()).ShouldContain("\"code\":\"validation\"");
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "unsaved");
        (await GetProseAsync()).Version.ShouldBe(before.Version);
    }
}
