using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Codex.Contracts.Errors;

namespace Codex.AcceptanceTests.Lanes.References;

// The Velocity Trap quotes 342 words against a limit of 300: it cannot be cleared until the risk is gone.
[TestFixture]
public class ClearanceBlockedTests : ReferencesLaneTest
{
    private static Guid Source => SeedIds.References.VelocityTrap;

    [Test, Order(1)]
    public async Task Panel_WhenARiskIsOpen_ShouldOfferNoSignOff()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Quoted prose: 342 of 300 words");
        await Expect(panel.Check("words")).ToHaveAttributeAsync("data-status", "danger");
        await Expect(panel.VerdictAction).ToHaveCountAsync(0);
    }

    [Test, Order(2)]
    public async Task SignOff_WhenARiskIsOpen_ShouldBeRefusedWith422Blocked()
    {
        await Page.GotoAsync(SourceLink(Source));
        await Expect(new SourcePanel(Page).VerdictStatus).ToHaveTextAsync("Copyright risk");

        var response = await Page.APIRequest.PostAsync($"{BaseUrl}/api/references/{Source}/sign-off");

        response.Status.ShouldBe(422);
        (await response.JsonAsync())!.Value.GetProperty("code").GetString().ShouldBe(ApiErrorCodes.Blocked);
    }

    [Test, Order(3)]
    public async Task Panel_WhenReloadedAfterTheRefusal_ShouldStillBeARisk()
    {
        await Page.GotoAsync(SourceLink(Source));

        await Expect(new SourcePanel(Page).VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(new ReferencesScreen(Page).Chip("risk")).ToContainTextAsync("3");
    }
}
