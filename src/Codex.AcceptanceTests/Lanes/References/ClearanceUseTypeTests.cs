using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.References;

// Re-typing one of the Velocity Trap's two quotes brings it under the limit: the risk, and the book bar badge, drop.
[TestFixture]
public class ClearanceUseTypeTests : ReferencesLaneTest
{
    [Test, Order(1)]
    public async Task UseType_WhenAQuoteBecomesAParaphrase_ShouldLiftTheRiskAndRefreshTheBadge()
    {
        var source = SeedIds.References.VelocityTrap;
        var use = SeedIds.ReferenceUses.VelocityTrapCh4Sc1;
        await Page.GotoAsync(SourceLink(source));
        var panel = new SourcePanel(Page);
        var bar = new AppShell(Page).BookBar;
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");

        var response = await Page.RunAndWaitForApiAsync($"/api/reference-uses/{use}", "PATCH", () => panel.UseKind(use).SelectOptionAsync("paraphrase"));

        response.Status.ShouldBe(200);
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("2");
        await Expect(new ReferencesScreen(Page).Chip("risk")).ToContainTextAsync("2");
    }

    [Test, Order(2)]
    public async Task UseType_WhenTheServerRefuses_ShouldKeepTheOldTypeAndSayWhy()
    {
        var use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;
        await Page.GotoAsync(SourceLink(SeedIds.References.VelocityTrap));
        var panel = new SourcePanel(Page);
        await Expect(panel.UseKind(use)).ToHaveValueAsync("quote");
        await Page.RouteAsync($"**/api/reference-uses/{use}", route => route.FulfillAsync(new()
        {
            Status = 400,
            ContentType = "application/json",
            Body = """{"code":"validation","error":"Use type 'lyric' is not allowed here."}""",
        }));

        await Page.RunAndWaitForApiAsync($"/api/reference-uses/{use}", "PATCH", () => panel.UseKind(use).SelectOptionAsync("lyric"));

        await Expect(panel.Banner).ToHaveTextAsync("Use type 'lyric' is not allowed here.");
        await Expect(panel.UseKind(use)).ToHaveValueAsync("quote");
    }
}
