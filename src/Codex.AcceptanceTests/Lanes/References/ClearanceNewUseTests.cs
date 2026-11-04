using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.References;

// Accelerate has a use newer than its sign-off (Ch 5 Sc 2, 3 days old, against a sign-off 8 days old).
[TestFixture]
public class ClearanceNewUseTests : ReferencesLaneTest
{
    private static Guid Source => SeedIds.References.Accelerate;

    [Test, Order(1)]
    public async Task Panel_WhenAccelerateOpened_ShouldFlagTheNewUseAndOfferToClearAgain()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await Expect(panel.Title).ToHaveTextAsync("Accelerate");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(panel.VerdictReason).ToHaveTextAsync("New uses since cleared");
        await Expect(panel.VerdictAction).ToHaveTextAsync("Clear again");
        await Expect(panel.UseNew(SeedIds.ReferenceUses.AccelerateCh5Sc2)).ToBeVisibleAsync();
        await Expect(panel.UseNew(SeedIds.ReferenceUses.AccelerateCh7Sc1)).ToHaveCountAsync(0);
    }

    [Test, Order(2)]
    public async Task Panel_WhenOpenInWriteShown_ShouldLinkToTheSceneAndTheSource()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await Expect(panel.UseWrite(SeedIds.ReferenceUses.AccelerateCh5Sc2))
            .ToHaveAttributeAsync("href", Links.Write(Book, Links.Hash(("scene", "5.2"), ("ref", Source.ToString()))));
    }

    [Test, Order(3)]
    public async Task SignOff_WhenClickedOnTheNewUse_ShouldClearTheSourceAndUpdateTheStrip()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        await Expect(panel.VerdictAction).ToHaveTextAsync("Clear again");

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Source}/sign-off", "POST", () => panel.VerdictAction.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
        await Expect(panel.SignedOff).ToContainTextAsync("Joel Dickson");
        await Expect(panel.UseNew(SeedIds.ReferenceUses.AccelerateCh5Sc2)).ToHaveCountAsync(0);
        await Expect(panel.HistoryItems.First).ToContainTextAsync("marked the source as cleared");
        await Expect(screen.ClearedText).ToHaveTextAsync("5 of 11 cited sources cleared");
        await Expect(screen.Row(Source).GetByTestId(ReferencesTestIds.RowStatus)).ToContainTextAsync("Cleared");
    }

    [Test, Order(4)]
    public async Task Reopen_WhenClicked_ShouldPutTheSourceBackToReadyToSignOff()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        await Expect(panel.VerdictAction).ToHaveTextAsync("Reopen");

        await Page.RunAndWaitForApiAsync($"/api/references/{Source}/reopen", "POST", () => panel.VerdictAction.ClickAsync());

        await Expect(panel.VerdictReason).ToHaveTextAsync("Ready to sign off");
        await Expect(new ReferencesScreen(Page).ClearedText).ToHaveTextAsync("4 of 11 cited sources cleared");
    }
}
