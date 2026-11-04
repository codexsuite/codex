using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Kb;

// Deleting seed entries that are mentioned and linked in one scene each: Kent Beck (scene 3.1) for the warning and
// the plain text, Marty Cagan (5.1) for Undo, Conway's Law (9.1) for an Undo that is refused because the scene
// changed. Steps run in order on one cloned database.
[TestFixture]
public class KbDeleteTests : SeededAcceptanceTest
{
    private static string BookId => SeedIds.Books.ProductEngineering.ToString();

    private KbEntryWindow Window => new(Page);

    private static string EntryUrl(Guid entry) => Links.Plan(BookId, Links.Hash(("kb", entry.ToString()), ("entry", entry.ToString())));

    private async Task<(string Body, int Version)> ProseAsync(Guid scene)
    {
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/api/scenes/{scene}/prose");
        using var json = JsonDocument.Parse(await response.TextAsync());
        return (json.RootElement.GetProperty("body").GetString()!, json.RootElement.GetProperty("version").GetInt32());
    }

    private async Task OpenDeleteDialogAsync(Guid entry, string name)
    {
        await Page.GotoAsync(EntryUrl(entry));
        await Expect(Window.Title).ToHaveTextAsync(name);
        await Window.Menu.ClickAsync();
        await Window.MenuDelete.ClickAsync();
        await Expect(Window.DeleteMessage).ToContainTextAsync("It is used in 1 scene.");
    }

    private async Task DeleteAsync(Guid entry, string name)
    {
        await OpenDeleteDialogAsync(entry, name);
        var deleted = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{entry}", "DELETE", () => Window.DeleteConfirm.ClickAsync());
        deleted.Status.ShouldBe(200);
        await Expect(Window.Root).ToHaveCountAsync(0);
        await Expect(Window.Toast).ToContainTextAsync($"Deleted {name}");
    }

    [Test, Order(1)]
    public async Task Delete_ShouldWarnWithUseCount()
    {
        await OpenDeleteDialogAsync(SeedIds.KbEntries.KentBeck, "Kent Beck");

        await Expect(Window.DeleteMessage).ToContainTextAsync("Mentions turn into plain text");
        await Window.DeleteCancel.ClickAsync();
        await Expect(Window.DeleteDialog).ToHaveCountAsync(0);
        (await ProseAsync(SeedIds.Scenes.Ch3Sc1)).Body.ShouldContain($"(kb:{SeedIds.KbEntries.KentBeck})");
    }

    [Test, Order(2)]
    public async Task Delete_ShouldTurnMentionsToPlainText()
    {
        await DeleteAsync(SeedIds.KbEntries.KentBeck, "Kent Beck");

        var (body, _) = await ProseAsync(SeedIds.Scenes.Ch3Sc1);
        body.ShouldNotContain($"kb:{SeedIds.KbEntries.KentBeck}");
        body.ShouldContain("Kent Beck");
        await Expect(new KbSidebar(Page).Item(SeedIds.KbEntries.KentBeck)).ToHaveCountAsync(0);
    }

    [Test, Order(3)]
    public async Task Delete_WhenAlreadyDeleted_ShouldReturn404()
    {
        var response = await Page.APIRequest.DeleteAsync($"{BaseUrl}/api/kb/entries/{SeedIds.KbEntries.KentBeck}");

        response.Status.ShouldBe(404);
    }

    [Test, Order(4)]
    public async Task Undo_ShouldRestoreMentions()
    {
        var (before, _) = await ProseAsync(SeedIds.Scenes.Ch5Sc1);
        await DeleteAsync(SeedIds.KbEntries.MartyCagan, "Marty Cagan");
        (await ProseAsync(SeedIds.Scenes.Ch5Sc1)).Body.ShouldNotContain($"kb:{SeedIds.KbEntries.MartyCagan}");

        var restored = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{SeedIds.KbEntries.MartyCagan}/restore", "POST", () =>
            Window.ToastUndo.ClickAsync());

        restored.Status.ShouldBe(200);
        (await ProseAsync(SeedIds.Scenes.Ch5Sc1)).Body.ShouldBe(before);
        await Expect(new KbSidebar(Page).Item(SeedIds.KbEntries.MartyCagan)).ToBeVisibleAsync();
    }

    [Test, Order(5)]
    public async Task Undo_WhenTheSceneChanged_ShouldBeRefusedAndSayWhy()
    {
        await DeleteAsync(SeedIds.KbEntries.ConwaysLaw, "Conway's Law");
        var (body, version) = await ProseAsync(SeedIds.Scenes.Ch9Sc1);
        var edited = await Page.APIRequest.PutAsync(
            $"{BaseUrl}/api/scenes/{SeedIds.Scenes.Ch9Sc1}/prose", new() { DataObject = new { body = body + "\n\nOne more line.", version } });
        edited.Status.ShouldBe(200);

        var restored = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{SeedIds.KbEntries.ConwaysLaw}/restore", "POST", () =>
            Window.ToastUndo.ClickAsync());

        restored.Status.ShouldBe(409);
        await Expect(Window.Toast).ToContainTextAsync("stays deleted");
        (await Page.APIRequest.GetAsync($"{BaseUrl}/api/kb/entries/{SeedIds.KbEntries.ConwaysLaw}")).Status.ShouldBe(404);
    }
}
