using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.Contracts.Storage;
using Codex.Core;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Codex.TestSupport.Database;
using Npgsql;

namespace Codex.AcceptanceTests.Lanes.References;

// Team Topologies' figure request was sent 16 days ago. The reply grants it: the signed letter and a credit line
// are kept, the risk lifts, and the bibliography prints the credit line.
[TestFixture]
public class PermissionGrantedTests : ReferencesLaneTest
{
    private static readonly byte[] Letter = "%PDF-1.4 signed letter"u8.ToArray();

    private static Guid Source => SeedIds.References.TeamTopologies;

    private ClonedDatabase _clone = null!;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IBlobStore>(new InMemoryBlobStore());

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        _clone = await base.CloneDatabaseAsync();
        return _clone;
    }

    // For instants created during the run (a response recorded by this test).
    private static string Day(TimeSpan offset)
    {
        var now = TimeProvider.System.GetUtcNow();
        return DisplayDate.ShortDay(now + offset, now, TimeProvider.System.LocalTimeZone);
    }

    // The seeded request's sent and reminder days. The seed stamps them when the template database is built, not at
    // test time, so they are read back rather than computed from now.
    private async Task<(string Sent, string Remind)> SeededDaysAsync()
    {
        await using var connection = await _clone.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT sent_at, remind_at FROM permission_request WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", SeedIds.PermissionRequests.TeamTopologies);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        var now = TimeProvider.System.GetUtcNow();
        string Label(int column) =>
            DisplayDate.ShortDay(reader.GetFieldValue<DateTimeOffset>(column), now, TimeProvider.System.LocalTimeZone);
        return (Label(0), Label(1));
    }

    [Test, Order(1)]
    public async Task Panel_WhenTheRequestWasSent_ShouldShowTheDateAndTheReminder()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        var (sent, remind) = await SeededDaysAsync();

        await Expect(panel.PermSentLabel).ToHaveTextAsync($"Asked {sent} · no reply");
        await Expect(panel.PermRemindLabel).ToHaveTextAsync($"Reminder {remind}");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission asked, no reply");
    }

    [Test, Order(2)]
    public async Task Grant_WithoutALetter_ShouldNotBeOffered()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);

        await panel.PermGranted.ClickAsync();

        await Expect(panel.PermGrantSubmit).ToBeDisabledAsync();
    }

    [Test, Order(3)]
    public async Task Grant_WithAnOversizeLetter_ShouldShowTheServersMessageAndStaySent()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        await panel.PermGranted.ClickAsync();
        await panel.PermFile.SetInputFilesAsync(new FilePayload { Name = "big.pdf", MimeType = "application/pdf", Buffer = new byte[(10 * 1024 * 1024) + 1] });

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{SeedIds.PermissionRequests.TeamTopologies}/granted", "POST", () => panel.PermGrantSubmit.ClickAsync());

        response.Status.ShouldBe(400);
        await Expect(panel.Banner).ToContainTextAsync("larger than 10 MB");
        await Expect(panel.Permission).ToHaveAttributeAsync("data-status", "sent");
    }

    [Test, Order(4)]
    public async Task Granted_WithLetter_ShouldAddCreditLineAndClear()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        var bar = new AppShell(Page).BookBar;
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");
        await panel.PermGranted.ClickAsync();
        await panel.PermFile.SetInputFilesAsync(new FilePayload { Name = "it-revolution.pdf", MimeType = "application/pdf", Buffer = Letter });
        await panel.PermCreditInput.FillAsync("Used by permission of IT Revolution.");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{SeedIds.PermissionRequests.TeamTopologies}/granted", "POST", () => panel.PermGrantSubmit.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.PermStatus).ToHaveTextAsync("Permission granted");
        await Expect(panel.PermRespondedLabel).ToHaveTextAsync($"Granted {Day(TimeSpan.Zero)}");
        await Expect(panel.PermCredit).ToHaveTextAsync("Credit line: Used by permission of IT Revolution.");
        await Expect(panel.PermAttachment).ToHaveTextAsync("it-revolution.pdf");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("2");
    }

    [Test, Order(5)]
    public async Task Letter_ShouldDownloadAsUploaded()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        await Expect(panel.PermAttachment).ToBeVisibleAsync();
        var href = await panel.PermAttachment.GetAttributeAsync("href");

        var response = await Page.APIRequest.GetAsync($"{BaseUrl}{href}");

        response.Status.ShouldBe(200);
        (await response.BodyAsync()).ShouldBe(Letter);
    }

    [Test, Order(6)]
    public async Task Bibliography_ShouldPrintTheCreditLineAfterTheEntry()
    {
        await Page.GotoAsync(Links.References(Book, null));
        var screen = new ReferencesScreen(Page);

        await screen.ViewBibliography.ClickAsync();

        await Expect(screen.BibliographyCredits).ToHaveTextAsync("Used by permission of IT Revolution.");
    }
}
