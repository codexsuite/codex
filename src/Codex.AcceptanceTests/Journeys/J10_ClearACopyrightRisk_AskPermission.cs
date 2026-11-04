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

// J10 · Clear a copyright risk (#16 Part 2), path 2 "ask permission", instead of paraphrasing: Draft request writes the
// letter from the uses, it is edited and marked sent (the risk stays, with "Permission asked"), and when the reply
// comes it is marked Granted with the letter attached and a credit line, which lifts the risk to Needs check.
[TestFixture, Category("J10")]
public class J10_ClearACopyrightRisk_AskPermission : JourneyTest
{
    private static readonly byte[] Letter = "%PDF-1.4 signed letter"u8.ToArray();

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Velocity = SeedIds.References.VelocityTrap.ToString();

    private static Guid _permissionId;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IBlobStore>(new InMemoryBlobStore());

    private static string ReferencesUrl => Links.References(BookId, Links.Hash((Velocity, null)));

    private static string Today()
    {
        var now = TimeProvider.System.GetUtcNow();
        return DisplayDate.ShortDay(now, now, TimeProvider.System.LocalTimeZone);
    }

    [Test, Order(1)]
    public async Task Step1_WhenOpenedFromTheReferencesBadge_ShouldShowCopyrightRiskOn342Of300Words()
    {
        var bar = new AppShell(Page).BookBar;
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");

        await bar.TabReferences.ClickAsync();
        await new ReferencesScreen(Page).Row(SeedIds.References.VelocityTrap).ClickAsync();

        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Quoted prose: 342 of 300 words");
    }

    [Test, Order(2)]
    public async Task Step2_WhenDraftRequestClicked_ShouldWriteTheLetterFromTheUses()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Velocity}/permission", "POST", () => panel.PermDraft.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(panel.PermLetter).ToHaveValueAsync(new Regex("342 words of quoted prose"));
        await Expect(panel.PermLetter).ToHaveValueAsync(new Regex("Chapters 4 and 8"));
        // Deviation: the letter writes the locators as "pp. 41-43 and p. 118". It carries no print run: the seed has none.
        await Expect(panel.PermLetter).ToHaveValueAsync(new Regex(@"pp\. 41.43 and p\. 118"));
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission drafted");
        _permissionId = (await ScalarSqlAsync<Guid>("SELECT id FROM permission_request WHERE reference_id = @id", ("id", SeedIds.References.VelocityTrap)));
    }

    [Test, Order(3)]
    public async Task Step3_WhenLetterEditedAndMarkedSent_ShouldStayCopyrightRiskWithPermissionAsked()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);
        await panel.PermTo.FillAsync("permissions@velocity.example");
        await panel.PermLetter.FillAsync("Dear Ms Reinertsen,\n\nMay I quote 342 words of The Velocity Trap?");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{_permissionId}/sent", "POST", () => panel.PermSend.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.PermSentLabel).ToHaveTextAsync($"Asked {Today()} · no reply");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission asked, no reply");
    }

    [Test, Order(4)]
    public async Task Step4_WhenReplyGrantedWithLetterAndCredit_ShouldLiftTheRiskAndAddTheCreditLine()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(ReferencesUrl);
        await panel.PermGranted.ClickAsync();
        await panel.PermFile.SetInputFilesAsync(new FilePayload { Name = "velocity-trap.pdf", MimeType = "application/pdf", Buffer = Letter });
        await panel.PermCreditInput.FillAsync("Used by permission of the publisher.");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{_permissionId}/granted", "POST", () => panel.PermGrantSubmit.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(panel.PermStatus).ToHaveTextAsync("Permission granted");
        await Expect(panel.PermAttachment).ToHaveTextAsync("velocity-trap.pdf");
        await Expect(panel.PermCredit).ToHaveTextAsync("Credit line: Used by permission of the publisher.");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("2");

        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(Links.References(BookId));
        await screen.ViewBibliography.ClickAsync();
        await Expect(screen.BibliographyCredits.Filter(new() { HasText = "Used by permission of the publisher." })).ToBeVisibleAsync();
    }
}
