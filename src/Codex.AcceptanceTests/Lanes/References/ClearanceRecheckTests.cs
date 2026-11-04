using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Lanes.References;

// DORA's address redirects. Re-check runs over the fake lookup; accepting the redirect replaces the address.
[TestFixture]
public class ClearanceRecheckTests : ReferencesLaneTest
{
    private const string OldAddress = "https://dora.dev/research/2024/";
    private const string NewAddress = "https://dora.dev/research/2024/dora-report/";

    private static Guid Source => SeedIds.References.DoraReport2024;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    [Test, Order(1)]
    public async Task Panel_WhenDoraOpened_ShouldShowTheRedirectAndAskForALicence()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await Expect(panel.Field("url")).ToHaveValueAsync(OldAddress);
        await Expect(panel.Verification("url")).ToHaveTextAsync("Redirects");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Licence not recorded");
        await Expect(panel.VerdictAction).ToHaveTextAsync("Set licence");
    }

    [Test, Order(2)]
    public async Task Recheck_WhenClicked_ShouldReportTheRedirectFromTheLookup()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Source}/recheck", "POST", () => panel.Recheck.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.Verification("url")).ToHaveTextAsync("Redirects");
        await Expect(panel.HistoryItems.First).ToContainTextAsync("re-checked URL");
    }

    [Test, Order(3)]
    public async Task Accept_WhenUpdateItClicked_ShouldReplaceTheAddressAndVerifyIt()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Source}/recheck/accept", "POST", () => panel.UpdateAddress("url").ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.Field("url")).ToHaveValueAsync(NewAddress);
        await Expect(panel.Verification("url")).ToHaveTextAsync("Verified");
        await Expect(panel.UpdateAddress("url")).ToHaveCountAsync(0);
    }

    [Test, Order(4)]
    public async Task Details_WhenLicenceAndAccessedDateAreFilledIn_ShouldMoveDoraToReadyToSignOff()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await Page.RunAndWaitForApiAsync($"/api/references/{Source}", "PATCH", () => panel.Field("licence").SelectOptionAsync("cc_by_4"));

        await Expect(panel.VerdictReason).ToHaveTextAsync("Citation incomplete");
        await Expect(panel.Field("accessedOn")).ToHaveAttributeAsync("data-missing", "true");
        await Page.RunAndWaitForApiAsync($"/api/references/{Source}", "PATCH", async () =>
        {
            await panel.Field("accessedOn").FillAsync("2024-11-02");
            await panel.Field("accessedOn").BlurAsync();
        });
        await Expect(panel.VerdictReason).ToHaveTextAsync("Ready to sign off");
        await Expect(panel.VerdictAction).ToHaveTextAsync("Mark as cleared");
    }
}
