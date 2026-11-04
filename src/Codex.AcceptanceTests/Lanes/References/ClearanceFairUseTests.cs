using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.References;

// The Manifesto's licence needs a fair-use decision before it can be signed off.
[TestFixture]
public class ClearanceFairUseTests : ReferencesLaneTest
{
    private static Guid Source => SeedIds.References.AgileManifestoWeb;

    [Test, Order(1)]
    public async Task FairUse_WhenAccepted_ShouldMoveTheManifestoToReadyToSignOff()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        await Expect(panel.VerdictReason).ToHaveTextAsync("Fair use not decided");
        await Expect(panel.VerdictAction).ToHaveTextAsync("Accept as fair use");

        await panel.VerdictAction.ClickAsync();
        await Expect(panel.FairUseAccept).ToBeDisabledAsync();
        await panel.FairUseReason.FillAsync("Seven words, quoted to be discussed.");
        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Source}/fair-use", "POST", () => panel.FairUseAccept.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.VerdictReason).ToHaveTextAsync("Ready to sign off");
        await Expect(panel.Check("licence")).ToContainTextAsync("Seven words, quoted to be discussed.");
        await Expect(panel.HistoryItems.First).ToContainTextAsync("accepted fair use");
    }

    [Test, Order(2)]
    public async Task SignOff_WhenFairUseDecided_ShouldClearTheManifesto()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await Page.RunAndWaitForApiAsync($"/api/references/{Source}/sign-off", "POST", () => panel.VerdictAction.ClickAsync());

        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
        await Expect(new ReferencesScreen(Page).ClearedText).ToHaveTextAsync("5 of 11 cited sources cleared");
    }
}
