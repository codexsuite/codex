using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.Core;
using Codex.Contracts.Storage;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Journeys;

// J25 negative path (docs/journeys-wave5.md): what must not be written. One fresh clone; the tests run in order and the
// withdraw, which changes the request for good, comes last.
[TestFixture, Category("J25")]
public class J25_PermissionFollowUp_Negative : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Source = SeedIds.References.TeamTopologies;
    private static readonly Guid Request = SeedIds.PermissionRequests.TeamTopologies;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IBlobStore>(new InMemoryBlobStore());

    private static string SourceLink(Guid id) => Links.References(BookId, Links.Hash((id.ToString(), null)));

    private async Task<DateTime> RemindAtAsync() =>
        await ScalarSqlAsync<DateTime>("SELECT remind_at FROM permission_request WHERE id = @id", ("id", Request));

    [Test, Order(1)]
    public async Task Remind_WhenTheDateIsInThePast_ShouldBeRejectedAndLeaveTheReminderAlone()
    {
        var panel = new SourcePanel(Page);
        var before = await RemindAtAsync();
        var yesterday = DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone).AddDays(-1).ToString("yyyy-MM-dd");
        await Page.GotoAsync(SourceLink(Source));

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}", "PATCH", () => panel.PermRemind.FillAsync(yesterday));

        response.Status.ShouldBe(400);
        await Expect(panel.Banner).ToHaveTextAsync("The reminder date cannot be in the past.");
        (await RemindAtAsync()).ShouldBe(before);
    }

    [Test, Order(2)]
    public async Task Grant_WhenTheFeeIsNegative_ShouldShowAnInlineErrorAndKeepTheRequestSent()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(Source));
        await panel.PermGranted.ClickAsync();
        await panel.PermFile.SetInputFilesAsync(new FilePayload { Name = "grant.pdf", MimeType = "application/pdf", Buffer = "%PDF-1.4"u8.ToArray() });
        await panel.PermFeeInput.FillAsync("-5");

        await panel.PermGrantSubmit.ClickAsync();

        await Expect(panel.PermFormError).ToContainTextAsync("zero or more");
        (await ScalarSqlAsync<string>("SELECT status FROM permission_request WHERE id = @id", ("id", Request))).ShouldBe("sent");
    }

    [Test, Order(3)]
    public async Task Grant_WhenTheFormIsCancelled_ShouldKeepTheRequestSentWithNoAttachment()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(Source));
        await panel.PermGranted.ClickAsync();
        await panel.PermFile.SetInputFilesAsync(new FilePayload { Name = "grant.pdf", MimeType = "application/pdf", Buffer = "%PDF-1.4"u8.ToArray() });

        await panel.PermGrantCancel.ClickAsync();

        await Expect(panel.PermGrantForm).ToHaveCountAsync(0);
        await Expect(panel.Permission).ToHaveAttributeAsync("data-status", "sent");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference_attachment WHERE permission_request_id = @id", ("id", Request))).ShouldBe(0);
    }

    [Test, Order(4)]
    public async Task Draft_WhileARequestIsSent_ShouldNotBeOffered()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(Source));

        await Expect(panel.Permission).ToHaveAttributeAsync("data-status", "sent");

        await Expect(panel.PermDraft).ToHaveCountAsync(0);
    }

    [Test, Order(5)]
    public async Task Refuse_WithoutANote_ShouldBeAllowed()
    {
        var panel = new SourcePanel(Page);
        var lyric = SeedIds.References.ShipItFriday;
        await Page.GotoAsync(SourceLink(lyric));
        await Page.RunAndWaitForApiAsync($"/api/references/{lyric}/permission", "POST", () => panel.PermDraft.ClickAsync());
        await panel.PermTo.FillAsync("rights@standup.example");
        var id = await ScalarSqlAsync<Guid>("SELECT id FROM permission_request WHERE reference_id = @id", ("id", lyric));
        await Page.RunAndWaitForApiAsync($"/api/permission-requests/{id}/sent", "POST", () => panel.PermSend.ClickAsync());
        await panel.PermRefused.ClickAsync();

        await Page.RunAndWaitForApiAsync($"/api/permission-requests/{id}/refused", "POST", () => panel.PermRefuseSubmit.ClickAsync());

        await Expect(panel.PermStatus).ToHaveTextAsync("Permission refused");
        (await ScalarSqlAsync<string>("SELECT status FROM permission_request WHERE id = @id", ("id", id))).ShouldBe("refused");
    }

    [Test, Order(6)]
    public async Task Withdraw_WhenTheRequestIsSent_ShouldMarkItWithdrawnKeepTheRiskAndClearTheReminderChip()
    {
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        await ScalarSqlAsync<int>("UPDATE permission_request SET remind_at = now() - interval '1 day' WHERE id = @id RETURNING 1", ("id", Request));
        await Page.GotoAsync(Links.References(BookId));
        await Expect(screen.RemindersChip).ToHaveTextAsync("1 reply overdue");
        await Page.GotoAsync(SourceLink(Source));

        await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}/withdraw", "POST", () => panel.PermWithdraw.ClickAsync());

        (await ScalarSqlAsync<string>("SELECT status FROM permission_request WHERE id = @id", ("id", Request))).ShouldBe("withdrawn");
        await Page.GotoAsync(Links.References(BookId));
        await Expect(screen.RemindersChip).ToHaveCountAsync(0);
        await Expect(screen.Row(Source).GetByTestId(ReferencesTestIds.RowStatus)).ToHaveTextAsync("Copyright risk");
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("3");
    }
}
