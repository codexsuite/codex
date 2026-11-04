using System.Net;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Write;

// Editing the manuscript in place: autosave through PUT prose, a stale save (last write wins, R6) and a failed save.
[TestFixture]
public class WriteEditTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid SceneId = SeedIds.Scenes.Ch5Sc1;
    private static readonly string ProsePath = $"/api/scenes/{SceneId}/prose";

    private async Task TypeAtStartAsync(WritePageObject write, string text)
    {
        await write.ProseEditor(SceneId).ClickAsync();
        await Page.Keyboard.PressAsync("Control+Home");
        await Page.Keyboard.TypeAsync(text);
    }

    private async Task<ProseDto> GetProseAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>(ProsePath))!;
    }

    [Test, Order(1)]
    public async Task Typing_ShouldAutosaveAndShowSaved()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(SceneId)).ToBeVisibleAsync();
        var before = await GetProseAsync();

        var response = await Page.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await TypeAtStartAsync(write, "Autosaved opening. ");
            await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "unsaved");
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(write.SaveStatus).ToHaveTextAsync("Saved");
        var after = await GetProseAsync();
        after.Version.ShouldBe(before.Version + 1);
        after.Body.ShouldStartWith("Autosaved opening. ");
    }

    [Test, Order(2)]
    public async Task Save_WhenVersionStale_ShouldWinAndKeepTheOtherTextInHistory()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(SceneId)).ToBeVisibleAsync();
        var current = await GetProseAsync();
        using (var client = new HttpClient { BaseAddress = new Uri(BaseUrl) })
        {
            var bump = await client.PutAsJsonAsync(ProsePath, new SaveProseRequest("Written by someone else.\n", current.Version));
            bump.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // R6: last write wins. The stale save is accepted, the bar says Saved, and the text it replaced is kept.
        var response = await Page.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await TypeAtStartAsync(write, "My edit. ");
            await write.Manuscript.FocusAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        (await GetProseAsync()).Body.ShouldStartWith("My edit. ");
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM prose_revision WHERE scene_id = @scene AND source = 'replaced' AND body = @body",
            ("scene", SceneId), ("body", "Written by someone else.\n"))).ShouldBe(1);
    }

    [Test, Order(3)]
    public async Task Save_WhenOffline_ShouldShowUnsaved()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(SceneId)).ToBeVisibleAsync();
        var before = await GetProseAsync();
        await Page.RouteAsync("**/api/scenes/*/prose", route =>
            string.Equals(route.Request.Method, "PUT", StringComparison.OrdinalIgnoreCase)
                ? route.AbortAsync("failed")
                : route.FallbackAsync());

        await TypeAtStartAsync(write, "Offline words. ");
        await write.Manuscript.FocusAsync();

        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "unsaved");
        await Expect(write.SaveStatus).ToHaveTextAsync("Unsaved");
        await Expect(write.ProseEditor(SceneId)).ToContainTextAsync("Offline words.");
        (await GetProseAsync()).Version.ShouldBe(before.Version);

        await Page.UnrouteAsync("**/api/scenes/*/prose");
        var response = await Page.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await TypeAtStartAsync(write, "Back online. ");
            await write.Manuscript.FocusAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        (await GetProseAsync()).Body.ShouldContain("Offline words.");
    }

    [Test, Order(4)]
    public async Task Summary_WhenEdited_ShouldPatchTheScene()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.SummaryEditor(SceneId)).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SceneId}", "PATCH", async () =>
        {
            await write.SummaryEditor(SceneId).ClickAsync();
            await Page.AfterEditorClickAsync();
            await Page.Keyboard.PressAsync("Control+End");
            await Page.Keyboard.TypeAsync(" And more.");
            await write.Manuscript.FocusAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Page.ReloadAsync();
        await Expect(write.SceneSummary(SceneId)).ToContainTextAsync("And more.");
    }
}
