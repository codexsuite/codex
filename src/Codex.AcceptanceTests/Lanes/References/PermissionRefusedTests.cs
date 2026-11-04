using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.References;

// Team Topologies' publisher refuses: the risk stays, the refusal is on record, and a new request can be drafted.
[TestFixture]
public class PermissionRefusedTests : ReferencesLaneTest
{
    private static Guid Source => SeedIds.References.TeamTopologies;

    [Test, Order(1)]
    public async Task Refused_ShouldKeepRisk()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        var bar = new AppShell(Page).BookBar;
        await panel.PermRefused.ClickAsync();
        await panel.PermRefuseNote.FillAsync("Not licensed for reuse.");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{SeedIds.PermissionRequests.TeamTopologies}/refused", "POST", () => panel.PermRefuseSubmit.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermStatus).ToHaveTextAsync("Permission refused");
        await Expect(panel.PermNote).ToHaveTextAsync("Not licensed for reuse.");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission refused");
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");
    }

    [Test, Order(2)]
    public async Task Refused_WhenAnsweredTwice_ShouldBeRefusedWith409()
    {
        var response = await Page.APIRequest.PostAsync($"{BaseUrl}/api/permission-requests/{SeedIds.PermissionRequests.TeamTopologies}/refused", new() { DataObject = new { note = "Again" } });

        response.Status.ShouldBe(409);
    }

    [Test, Order(3)]
    public async Task Draft_AfterARefusal_ShouldOfferANewRequest()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await Expect(panel.PermDraft).ToHaveTextAsync("Draft a new request");
        await panel.PermDraft.ClickAsync();

        await Expect(panel.PermLetter).ToHaveValueAsync(new System.Text.RegularExpressions.Regex("1 figure"));
    }
}
