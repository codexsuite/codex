using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Codex.Contracts.Citations;

namespace Codex.AcceptanceTests.Lanes.Cite;

// J11 step 3: in the References inbox, f2 (Ch 8 Sc 1, a 93% match with The Velocity Trap, p. 112) → Cite. Its words are
// wrapped as a 34-word quote use and the flag leaves the inbox; the source is still a risk.
[TestFixture]
public class CiteFromFlagTests : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid F2 = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch;

    [Test]
    public async Task Cite_WhenF2IsCited_ShouldAddA34WordUseAndLeaveTheInbox()
    {
        var inbox = new QuoteFlagsInbox(Page);
        await Page.GotoAsync(Links.References(BookId));
        await inbox.Button.ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/quote-flags/{F2}/cite", "POST",
            () => inbox.Item(F2).GetByTestId(ReferencesTestIds.FlagCite).ClickAsync());

        response.Ok.ShouldBeTrue();
        var result = JsonSerializer.Deserialize<CitePassageResult>(await response.TextAsync(), JsonSerializerOptions.Web)!;
        await Expect(inbox.Item(F2)).ToHaveCountAsync(0);
        await Expect(inbox.Button).ToHaveTextAsync("1 possible unflagged quote");
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(Links.References(BookId, Links.Hash((SeedIds.References.VelocityTrap.ToString(), null))));
        await Expect(panel.Use(result.UseId)).ToContainTextAsync("34 words · p. 112");
        await Expect(panel.UseKind(result.UseId)).ToHaveValueAsync("quote");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
    }
}
