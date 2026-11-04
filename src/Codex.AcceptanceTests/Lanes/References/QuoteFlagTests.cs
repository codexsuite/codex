using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Lanes.References;

// The seed has two open flags (Ch 6 Sc 1, Ch 8 Sc 1). A saved quote is flagged by a scan that runs after the save
// returns, so each step waits for the flags API to show it before loading the screen.
[TestFixture]
public class QuoteFlagTests : ReferencesLaneTest
{
    private const string FirstQuote = "ship nothing on a Friday afternoon";
    private const string SecondQuote = "the roadmap is a promise to ourselves";

    private async Task SaveProseAsync(Guid sceneId, string addition)
    {
        var url = $"{BaseUrl}/api/scenes/{sceneId}/prose";
        var current = (await (await Page.APIRequest.GetAsync(url)).JsonAsync())!.Value;
        var body = current.GetProperty("body").GetString() + addition;
        var response = await Page.APIRequest.PutAsync(url, new() { DataObject = new { body, version = current.GetProperty("version").GetInt32() } });
        response.Status.ShouldBe(200);
    }

    // The scan runs after the PUT returns: poll the flags API from the page (Playwright's own polling) until the count
    // is reached, then load the screen once.
    private async Task OpenInboxAfterScanAsync(int openFlags)
    {
        await Page.GotoAsync(Links.References(Book));
        await Page.WaitForFunctionAsync(
            "async ([url, count]) => (await (await fetch(url)).json()).length === count",
            new object[] { $"/api/books/{Book}/quote-flags", openFlags },
            new() { PollingInterval = 200 });
        await Page.ReloadAsync();
    }

    [Test, Order(1)]
    public async Task ProsePut_WithQuotedSentence_ShouldRaiseNewFlag()
    {
        await SaveProseAsync(SeedIds.Scenes.Ch7Sc1, $"\nA lead insists \"{FirstQuote}\" in every retro.\n");
        var inbox = new QuoteFlagsInbox(Page);

        await OpenInboxAfterScanAsync(3);
        await inbox.Button.ClickAsync();

        await Expect(inbox.Excerpts.Filter(new() { HasText = FirstQuote })).ToBeVisibleAsync();
        await Expect(inbox.Item(SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote)).ToBeVisibleAsync();
        await Expect(inbox.Item(SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch)).ToBeVisibleAsync();
        await Expect(inbox.Excerpts).ToHaveCountAsync(3);
    }

    [Test, Order(2)]
    public async Task NotAQuote_ShouldNeverReturn()
    {
        var inbox = new QuoteFlagsInbox(Page);
        await Page.GotoAsync(Links.References(Book));
        await inbox.Button.ClickAsync();

        var response = await Page.RunAndWaitForApiAsync(
            $"/api/quote-flags/{SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote}/not-a-quote", "POST", () => inbox.NotQuote(SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote).ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(inbox.Item(SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote)).ToHaveCountAsync(0);

        // Re-save the same scene with another quote: that scan sees both, and only the new one is raised.
        await SaveProseAsync(SeedIds.Scenes.Ch6Sc1, $"\nHe added \"{SecondQuote}\" with a shrug.\n");
        await OpenInboxAfterScanAsync(3);
        await inbox.Button.ClickAsync();

        await Expect(inbox.Excerpts.Filter(new() { HasText = SecondQuote })).ToBeVisibleAsync();
        await Expect(inbox.Excerpts.Filter(new() { HasText = "The customer is not in the building" })).ToHaveCountAsync(0);
        await Expect(inbox.Item(SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote)).ToHaveCountAsync(0);
    }

    [Test, Order(3)]
    public async Task Cite_WhenFlagHasAGuessedSource_ShouldBeOffered()
    {
        var inbox = new QuoteFlagsInbox(Page);
        await Page.GotoAsync(Links.References(Book));
        await inbox.Button.ClickAsync();

        var cite = inbox.Item(SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch).GetByTestId(ReferencesTestIds.FlagCite);

        await Expect(cite).ToBeEnabledAsync();
        await Expect(cite).ToHaveTextAsync("Cite Whitlock, 2021");
        await Expect(inbox.Item(SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch).GetByTestId(ReferencesTestIds.FlagWrite))
            .ToHaveAttributeAsync("href", Links.Write(Book, Links.Hash(("scene", "8.1"))));
    }
}
