using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.Contracts.Storage;
using Codex.Core;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Journeys;

// J25 · Permission follow-up (docs/journeys-wave5.md), the Granted path. Team Topologies' request was sent 16 days ago;
// the test moves its reminder a day into the past (time is database-stamped, D4), then snoozes and chases it and
// records the reply with the letter, a credit line and a fee, and swaps the letter for another.
[TestFixture, Category("J25")]
public class J25_PermissionFollowUp : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Source = SeedIds.References.TeamTopologies;
    private static readonly Guid Request = SeedIds.PermissionRequests.TeamTopologies;
    private static readonly byte[] Pdf = "%PDF-1.4 signed letter"u8.ToArray();

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IBlobStore>(new InMemoryBlobStore());

    private static string SourceLink => Links.References(BookId, Links.Hash((Source.ToString(), null)));

    private static string Today()
    {
        var now = TimeProvider.System.GetUtcNow();
        return DisplayDate.ShortDay(now, now, TimeProvider.System.LocalTimeZone);
    }

    [Test, Order(1)]
    public async Task Step1_WhenTheReminderIsOverdue_ShouldShowTheChipAndTheRowReminderButNotChangeTheBadge()
    {
        await ScalarSqlAsync<int>("UPDATE permission_request SET remind_at = now() - interval '1 day' WHERE id = @id RETURNING 1", ("id", Request));
        var screen = new ReferencesScreen(Page);

        await Page.GotoAsync(Links.References(BookId));

        await Expect(screen.RemindersChip).ToHaveTextAsync("1 reply overdue");
        await Expect(screen.RowReminder(Source)).ToHaveTextAsync("Reminder due");
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("3");
    }

    [Test, Order(2)]
    public async Task Step2_WhenSnoozedChasedAndMarkedFollowedUp_ShouldMoveTheReminderAndStampTheChaser()
    {
        var panel = new SourcePanel(Page);
        await Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await Page.GotoAsync(SourceLink);
        await Expect(panel.PermSentLabel).ToContainTextAsync("Asked ");
        await Expect(panel.PermRemindLabel).ToHaveTextAsync("Reminder due");

        await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}", "PATCH", () => panel.PermRemindIn(3).ClickAsync());

        await Expect(panel.PermRemindLabel).Not.ToHaveTextAsync("Reminder due");
        (await ScalarSqlAsync<bool>("SELECT remind_at > now() + interval '2 days' FROM permission_request WHERE id = @id", ("id", Request))).ShouldBeTrue();
        await panel.PermCopyFollowUp.ClickAsync();
        var copied = await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        copied.ShouldContain("Team Topologies");
        Regex.IsMatch(copied, @"request of \d{1,2} \w{3}").ShouldBeTrue();

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}/followed-up", "POST", () => panel.PermFollowedUp.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermFollowedUpLabel).ToHaveTextAsync($"Followed up {Today()}");
        (await ScalarSqlAsync<bool>("SELECT last_reminded_at IS NOT NULL FROM permission_request WHERE id = @id", ("id", Request))).ShouldBeTrue();
        await Page.GotoAsync(Links.References(BookId));
        await Expect(new ReferencesScreen(Page).RemindersChip).ToHaveCountAsync(0);
    }

    [Test, Order(3)]
    public async Task Step3_WhenTheReplyIsGrantedWithLetterCreditAndFee_ShouldLiftTheRiskAndPrintTheCreditLine()
    {
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(SourceLink);
        await panel.PermGranted.ClickAsync();
        await panel.PermFile.SetInputFilesAsync(new FilePayload { Name = "grant.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await panel.PermCreditInput.FillAsync("Reprinted by permission of IT Revolution");
        await panel.PermTermsInput.FillAsync("One edition, print only");
        await panel.PermFeeInput.FillAsync("25.50");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}/granted", "POST", () => panel.PermGrantSubmit.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermStatus).ToHaveTextAsync("Permission granted");
        await Expect(panel.PermRespondedLabel).ToHaveTextAsync($"Granted {Today()}");
        await Expect(panel.PermCredit).ToHaveTextAsync("Credit line: Reprinted by permission of IT Revolution");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(panel.HistoryItems.First).ToContainTextAsync("recorded that permission was granted");
        (await ScalarSqlAsync<string>("SELECT status FROM permission_request WHERE id = @id", ("id", Request))).ShouldBe("granted");
        (await ScalarSqlAsync<decimal>("SELECT fee_amount FROM permission_request WHERE id = @id", ("id", Request))).ShouldBe(25.50m);
        await Page.GotoAsync(Links.References(BookId));
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("2");
        await Expect(screen.Gate).ToContainTextAsync("2 risks block print export");
        await Expect(screen.RemindersChip).ToHaveCountAsync(0);
        await screen.ViewBibliography.ClickAsync();
        await Expect(screen.BibliographyCredits.Filter(new() { HasText = "Reprinted by permission of IT Revolution" })).ToBeVisibleAsync();
    }

    [Test, Order(4)]
    public async Task Step4_WhenTheLetterIsReplaced_ShouldLeaveExactlyOneAttachment()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink);
        await Expect(panel.PermAttachment).ToHaveTextAsync("grant.pdf");

        await panel.PermAttachmentRemove.ClickAsync();
        await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}/letter", "DELETE", () => panel.PermAttachmentConfirm.ClickAsync());
        await Expect(panel.PermAttachment).ToHaveCountAsync(0);
        await Page.RunAndWaitForApiAsync($"/api/permission-requests/{Request}/letter", "POST",
            () => panel.PermAttachInput.SetInputFilesAsync(new FilePayload { Name = "grant-v2.pdf", MimeType = "application/pdf", Buffer = Pdf }));

        await Expect(panel.PermAttachment).ToHaveTextAsync("grant-v2.pdf");
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM reference_attachment WHERE permission_request_id = @id AND kind = 'permission_letter' AND archived_at IS NULL", ("id", Request))).ShouldBe(1);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM reference_attachment WHERE permission_request_id = @id AND kind = 'permission_letter' AND archived_at IS NOT NULL", ("id", Request))).ShouldBe(1);
    }
}
