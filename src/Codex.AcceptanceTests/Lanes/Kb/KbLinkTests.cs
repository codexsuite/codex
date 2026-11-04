using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Kb;

// Related entries from the seed: link two entries in the picker, see the chip on both, unlink. Steps run in order
// on one cloned database.
[TestFixture]
public class KbLinkTests : SeededAcceptanceTest
{
    private static string BookId => SeedIds.Books.ProductEngineering.ToString();

    private KbEntryWindow Window => new(Page);

    private static string EntryUrl(Guid entryId) => Links.Plan(BookId, Links.Hash(("kb", entryId.ToString()), ("entry", entryId.ToString())));

    [Test, Order(1)]
    public async Task Related_WhenAdded_ShouldShowOnBothEntries()
    {
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.KentBeck));
        await Expect(Window.Title).ToHaveTextAsync("Kent Beck");
        await Expect(Window.Related).ToContainTextAsync("No related entries");

        await Window.RelatedAdd.ClickAsync();
        await Window.RelatedSearch.FillAsync("marty");
        var linked = await Page.RunAndWaitForApiAsync($"/api/kb/entries/{SeedIds.KbEntries.KentBeck}/related", "POST", () =>
            Window.RelatedOption(SeedIds.KbEntries.MartyCagan).ClickAsync());

        linked.Status.ShouldBe(200);
        await Expect(Window.RelatedChip(SeedIds.KbEntries.MartyCagan)).ToContainTextAsync("Marty Cagan");
        await Expect(Window.Toast).ToContainTextAsync("Linked Kent Beck and Marty Cagan");

        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.MartyCagan));
        await Expect(Window.Title).ToHaveTextAsync("Marty Cagan");
        await Expect(Window.RelatedChip(SeedIds.KbEntries.KentBeck)).ToContainTextAsync("Kent Beck");
    }

    [Test, Order(2)]
    public async Task Related_WhenLinkedToItself_ShouldBeRefusedByTheServer()
    {
        var response = await Page.APIRequest.PostAsync(
            $"{BaseUrl}/api/kb/entries/{SeedIds.KbEntries.KentBeck}/related",
            new() { DataObject = new { entryId = SeedIds.KbEntries.KentBeck } });

        response.Status.ShouldBe(400);
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.KentBeck));
        await Expect(Window.RelatedChip(SeedIds.KbEntries.MartyCagan)).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Unlink_ShouldRemoveChip()
    {
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.MartyCagan));
        await Expect(Window.RelatedChip(SeedIds.KbEntries.KentBeck)).ToBeVisibleAsync();

        var unlinked = await Page.RunAndWaitForApiAsync(
            $"/api/kb/entries/{SeedIds.KbEntries.MartyCagan}/related/{SeedIds.KbEntries.KentBeck}", "DELETE", async () =>
            {
                await Window.RelatedChip(SeedIds.KbEntries.KentBeck).HoverAsync();
                await Window.RelatedRemove(SeedIds.KbEntries.KentBeck).ClickAsync();
            });

        unlinked.Status.ShouldBe(200);
        await Expect(Window.RelatedChip(SeedIds.KbEntries.KentBeck)).ToHaveCountAsync(0);
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.KentBeck));
        await Expect(Window.Related).ToContainTextAsync("No related entries");
    }
}
