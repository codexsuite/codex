using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J11 · Triage unflagged quotes and new uses (#16 Part 2): the two open quote flags are resolved (one is not a quote,
// one is cited from the flag), Accelerate is signed off again for its new use, DORA's details are completed and its
// address re-checked, and the Manifesto's seven-word quote is accepted as fair use and signed off.
[TestFixture, Category("J11")]
public class J11_TriageUnflaggedQuotes : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Velocity = SeedIds.References.VelocityTrap;
    private static readonly Guid Accelerate = SeedIds.References.Accelerate;
    private static readonly Guid Dora = SeedIds.References.DoraReport2024;
    private static readonly Guid Manifesto = SeedIds.References.AgileManifestoWeb;
    private static readonly Guid Facilitator = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote;
    private static readonly Guid VelocityFlag = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    private static string SourceUrl(Guid source) => Links.References(BookId, Links.Hash((source.ToString(), null)));

    private async Task<string?> FlagStatusAsync(Guid flag) =>
        await ScalarSqlAsync<string>("SELECT status FROM quote_flag WHERE id = @id", ("id", flag));

    [Test, Order(1)]
    public async Task Step1_WhenReferencesOpened_ShouldShowTwoPossibleUnflaggedQuotes()
    {
        var inbox = new QuoteFlagsInbox(Page);
        await Page.GotoAsync(Links.References(BookId));

        await Expect(inbox.Button).ToHaveTextAsync("2 possible unflagged quotes");
        await inbox.Button.ClickAsync();

        await Expect(inbox.Excerpts).ToHaveCountAsync(2);
        await Expect(inbox.Item(Facilitator)).ToBeVisibleAsync();
        await Expect(inbox.Item(VelocityFlag)).ToBeVisibleAsync();
    }

    [Test, Order(2)]
    public async Task Step2_WhenF1MarkedNotAQuote_ShouldDropOutOfTheInboxAndStayDismissed()
    {
        var inbox = new QuoteFlagsInbox(Page);
        await Page.GotoAsync(Links.References(BookId));
        await inbox.Button.ClickAsync();
        await Expect(inbox.Item(Facilitator).GetByTestId(ReferencesTestIds.FlagExcerpt)).ToContainTextAsync("The customer is not in the building");
        await Expect(inbox.Item(Facilitator).GetByTestId(ReferencesTestIds.FlagScene)).ToHaveTextAsync("Chapter 6 · Scene 1");

        var response = await Page.RunAndWaitForApiAsync($"/api/quote-flags/{Facilitator}/not-a-quote", "POST", () => inbox.NotQuote(Facilitator).ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(inbox.Item(Facilitator)).ToHaveCountAsync(0);
        await Expect(inbox.Excerpts).ToHaveCountAsync(1);
        // "Never raised again": the dismissed fingerprint is kept (the re-scan itself is covered by QuoteFlagTests).
        (await FlagStatusAsync(Facilitator)).ShouldBe("dismissed");
        await Page.ReloadAsync();
        await Expect(inbox.Button).ToHaveTextAsync("1 possible unflagged quote");
    }

    [Test, Order(3)]
    public async Task Step3_WhenF2CitedFromTheFlag_ShouldAdd34WordQuoteAndLeaveVelocityTrapAt376()
    {
        var inbox = new QuoteFlagsInbox(Page);
        await Page.GotoAsync(Links.References(BookId));
        await inbox.Button.ClickAsync();
        await Expect(inbox.Item(VelocityFlag).GetByTestId(ReferencesTestIds.FlagScene)).ToHaveTextAsync("Chapter 8 · Scene 1");
        await Expect(inbox.Cite(VelocityFlag)).ToHaveTextAsync("Cite Whitlock, 2021");

        var response = await Page.RunAndWaitForApiAsync($"/api/quote-flags/{VelocityFlag}/cite", "POST", () => inbox.Cite(VelocityFlag).ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(inbox.Empty).ToBeVisibleAsync();
        (await FlagStatusAsync(VelocityFlag)).ShouldBe("cited");
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM reference_use WHERE reference_id = @id AND kind = 'quote' AND word_count = 34", ("id", Velocity))).ShouldBe(1);
        (await ScalarSqlAsync<long>(
            "SELECT COALESCE(sum(word_count), 0) FROM reference_use WHERE reference_id = @id AND kind = 'quote'", ("id", Velocity))).ShouldBe(376);

        // Still a risk (J10 clears it).
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceUrl(Velocity));
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Quoted prose: 376 of 300 words");
    }

    [Test, Order(4)]
    public async Task Step4_WhenNeedsCheckFilteredAndAccelerateSignedOffAgain_ShouldClearItsNewUse()
    {
        var screen = new ReferencesScreen(Page);
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(Links.References(BookId));

        await screen.Chip("needs_check").ClickAsync();

        await Expect(screen.Row(Accelerate)).ToBeVisibleAsync();
        await Expect(screen.Row(Velocity)).ToHaveCountAsync(0);
        await screen.Row(Accelerate).ClickAsync();
        await Expect(panel.Title).ToHaveTextAsync("Accelerate");
        await Expect(panel.VerdictReason).ToHaveTextAsync("New uses since cleared");
        await Expect(panel.UseNew(SeedIds.ReferenceUses.AccelerateCh5Sc2)).ToBeVisibleAsync();
        await Expect(panel.UseWrite(SeedIds.ReferenceUses.AccelerateCh5Sc2)).ToHaveAttributeAsync("href", new Regex(@"scene=5\.2"));

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Accelerate}/sign-off", "POST", () => panel.VerdictAction.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
        await Expect(panel.UseNew(SeedIds.ReferenceUses.AccelerateCh5Sc2)).ToHaveCountAsync(0);
    }

    [Test, Order(5)]
    public async Task Step5_WhenDoraDetailsFilledInAndAddressRechecked_ShouldAcceptTheRedirect()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceUrl(Dora));
        await Expect(panel.VerdictReason).ToHaveTextAsync("Licence not recorded");

        await Page.RunAndWaitForApiAsync($"/api/references/{Dora}", "PATCH", () => panel.Field("licence").SelectOptionAsync("cc_by_4"));
        await Page.RunAndWaitForApiAsync($"/api/references/{Dora}", "PATCH", async () =>
        {
            await panel.Field("rightsHolder").FillAsync("DORA, Google Cloud");
            await panel.Field("rightsHolder").BlurAsync();
        });
        await Page.RunAndWaitForApiAsync($"/api/references/{Dora}", "PATCH", async () =>
        {
            await panel.Field("accessedOn").FillAsync("2024-11-02");
            await panel.Field("accessedOn").BlurAsync();
        });
        // Deviation: the journey lists the details first and the re-check last; the verdict stays on "Identifier needs
        // checking" until the redirect is accepted, so "Ready to sign off" is asserted after the last action.
        await Expect(panel.VerdictReason).ToHaveTextAsync("Identifier needs checking");

        var rechecked = await Page.RunAndWaitForApiAsync($"/api/references/{Dora}/recheck", "POST", () => panel.Recheck.ClickAsync());
        rechecked.Ok.ShouldBeTrue();
        await Expect(panel.Verification("url")).ToHaveTextAsync("Redirects");
        var accepted = await Page.RunAndWaitForApiAsync($"/api/references/{Dora}/recheck/accept", "POST", () => panel.UpdateAddress("url").ClickAsync());

        accepted.Ok.ShouldBeTrue();
        await Expect(panel.Field("url")).ToHaveValueAsync("https://dora.dev/research/2024/dora-report/");
        await Expect(panel.Verification("url")).ToHaveTextAsync("Verified");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Ready to sign off");
        (await ScalarSqlAsync<string>("SELECT rights_holder FROM reference WHERE id = @id", ("id", Dora))).ShouldBe("DORA, Google Cloud");
    }

    [Test, Order(6)]
    public async Task Step6_WhenManifestoAcceptedAsFairUse_ShouldBeReadyToSignOffAndThenCleared()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceUrl(Manifesto));
        await Expect(panel.VerdictReason).ToHaveTextAsync("Fair use not decided");
        await panel.VerdictAction.ClickAsync();
        await panel.FairUseReason.FillAsync("Seven words, quoted to be discussed.");

        var accepted = await Page.RunAndWaitForApiAsync($"/api/references/{Manifesto}/fair-use", "POST", () => panel.FairUseAccept.ClickAsync());

        accepted.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Ready to sign off");
        var signed = await Page.RunAndWaitForApiAsync($"/api/references/{Manifesto}/sign-off", "POST", () => panel.VerdictAction.ClickAsync());

        signed.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
        await Expect(new QuoteFlagsInbox(Page).Button).ToHaveCountAsync(0);
    }
}
