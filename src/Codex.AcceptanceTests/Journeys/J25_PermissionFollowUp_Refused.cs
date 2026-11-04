using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J25 · Permission follow-up, the Refused path (steps 5 to 8) from its own fresh clone: the refusal keeps the risk and
// points at cutting the figure in Write; the request stays on file as refused, and a new one can be drafted.
[TestFixture, Category("J25")]
public class J25_PermissionFollowUp_Refused : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Source = SeedIds.References.TeamTopologies;
    private static readonly Guid Request = SeedIds.PermissionRequests.TeamTopologies;

    private static string SourceLink(Guid id) => Links.References(BookId, Links.Hash((id.ToString(), null)));

    [Test, Order(1)]
    public async Task Step5_WhenTheReplyIsRefused_ShouldKeepTheRiskAndOfferToCutTheFigure()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(Source));
        await panel.PermRefused.ClickAsync();
        await panel.PermRefuseNote.FillAsync("Declined: not licensing figures");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}/refused", "POST", () => panel.PermRefuseSubmit.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermStatus).ToHaveTextAsync("Permission refused");
        await Expect(panel.PermNote).ToHaveTextAsync("Declined: not licensing figures");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictCut).ToHaveTextAsync("Cut or replace the figure");
        (await ScalarSqlAsync<string>("SELECT status FROM permission_request WHERE id = @id", ("id", Request))).ShouldBe("refused");
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("3");
    }

    [Test, Order(2)]
    public async Task Step6_WhenTheFigureIsCutInWrite_ShouldMakeTheSourceNotCitedAndKeepTheRefusalOnFile()
    {
        var panel = new SourcePanel(Page);
        var write = new WritePageObject(Page);
        var cite = new CitePage(Page);
        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(SourceLink(Source));

        await panel.VerdictCut.ClickAsync();

        await Expect(write.MarkerCard).ToBeVisibleAsync();
        await Expect(write.MarkerCardSource).ToContainTextAsync("Team Topologies");
        var response = await Page.RunAndWaitForApiAsync($"/api/citations/{SeedIds.ReferenceUses.TeamTopologiesCh9Sc1}/remove", "POST", () => cite.Remove.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Page.GotoAsync(Links.References(BookId));
        await Expect(screen.Row(Source).GetByTestId(ReferencesTestIds.RowStatus)).ToHaveTextAsync("Not cited");
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("2");
        await Expect(screen.ClearedText).ToContainTextAsync("of 10 cited sources cleared");
        (await ScalarSqlAsync<string>("SELECT status FROM permission_request WHERE id = @id", ("id", Request))).ShouldBe("refused");
    }

    [Test, Order(3)]
    public async Task Step7_WhenAskedAgainAfterARefusal_ShouldDraftANewRequestNextToTheRefusedOne()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(Source));
        await Expect(panel.PermDraft).ToHaveTextAsync("Draft a new request");

        await Page.RunAndWaitForApiAsync($"/api/references/{Source}/permission", "POST", () => panel.PermDraft.ClickAsync());

        await Expect(panel.Permission).ToHaveAttributeAsync("data-status", "draft");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM permission_request WHERE reference_id = @id AND status = 'draft'", ("id", Source))).ShouldBe(1);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM permission_request WHERE reference_id = @id AND status = 'refused'", ("id", Source))).ShouldBe(1);
    }

    [Test, Order(4)]
    public async Task Step8_WhenTheLyricRequestIsDraftedAndSent_ShouldShowAReminderInFourteenDays()
    {
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        var lyric = SeedIds.References.ShipItFriday;
        await Page.GotoAsync(SourceLink(lyric));
        await Page.RunAndWaitForApiAsync($"/api/references/{lyric}/permission", "POST", () => panel.PermDraft.ClickAsync());
        await panel.PermTo.FillAsync("rights@standup.example");
        var id = await ScalarSqlAsync<Guid>("SELECT id FROM permission_request WHERE reference_id = @id", ("id", lyric));

        await Page.RunAndWaitForApiAsync($"/api/permission-requests/{id}/sent", "POST", () => panel.PermSend.ClickAsync());

        await Expect(panel.PermRemindLabel).ToContainTextAsync("Reminder ");
        await Page.GotoAsync(Links.References(BookId));
        await Expect(screen.RowReminder(lyric)).ToHaveTextAsync("Reminder due in 14 days");
    }
}
