using System.Net.Http.Json;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Cite;

// J10 path 1: The Velocity Trap is a copyright risk (342 of 300 quoted words). Paraphrase with AI on its Ch 8 quote
// opens Write at Ch 8 Sc 1 with a Scene beat after the quote asking for it in the author's words; the use is still a
// quote until the author changes its chip to Paraphrase, and then 214 quoted words are left: Needs check.
[TestFixture]
public class ParaphraseTests : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;
    private static readonly Guid Scene = SeedIds.Scenes.Ch8Sc1;
    private static readonly string Velocity = SeedIds.References.VelocityTrap.ToString();

    private sealed record BeatRow(Guid Id, Guid SceneId, string Body);

    private sealed record BeatsRow(List<BeatRow> Beats);

    [Test, Order(1)]
    public async Task Paraphrase_WhenAskedFromTheQuote_ShouldOpenWriteWithABeatAfterIt()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(Links.References(BookId, Links.Hash((Velocity, null))));
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");

        var instructed = await Page.RunAndWaitForResponseAsync(
            () => new CiteHandOffPage(Page).Paraphrase(Use).ClickAsync(),
            r => r.Request.Method == "PATCH" && new Uri(r.Url).AbsolutePath.StartsWith("/api/beats/", StringComparison.Ordinal));

        instructed.Ok.ShouldBeTrue();
        var write = new WritePageObject(Page);
        await Expect(Page).ToHaveURLAsync(BaseUrl + Links.Write(BookId, Links.Hash(("scene", "8.1"), ("ref", Velocity))));
        await Expect(write.BeatCards(Scene)).ToHaveCountAsync(2);
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var beats = (await client.GetFromJsonAsync<BeatsRow>($"/api/books/{BookId}/beats", JsonSerializerOptions.Web))!;
        beats.Beats.ShouldContain(b => b.SceneId == Scene && b.Body.StartsWith("Paraphrase the passage quoted from The Velocity Trap", StringComparison.Ordinal));
    }

    [Test, Order(2)]
    public async Task UseKind_WhenChangedToParaphraseAfterTheBeat_ShouldDropTheSourceToNeedsCheck()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(Links.References(BookId, Links.Hash((Velocity, null))));
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.UseKind(Use)).ToHaveValueAsync("quote");

        var patched = await Page.RunAndWaitForApiAsync($"/api/reference-uses/{Use}", "PATCH",
            () => panel.UseKind(Use).SelectOptionAsync("paraphrase"));

        patched.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(panel.UseKind(Use)).ToHaveValueAsync("paraphrase");
        await Expect(new CiteHandOffPage(Page).Paraphrase(Use)).ToHaveCountAsync(0);
    }
}
