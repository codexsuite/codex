using Codex.AcceptanceTests.Harness;
using Codex.Contracts;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;

namespace Codex.AcceptanceTests.Lanes.References;

// The References table over the seeded Product Engineering book (issue #16 section 1.6).
[TestFixture]
public class ReferencesTableTests : SeededAcceptanceTest
{
    private static string Book => SeedIds.Books.ProductEngineering.ToString();

    [Test, Order(1)]
    public async Task Strip_ShouldRead4Of11AndThreeRisks()
    {
        await Page.GotoAsync(Links.References(Book));
        var screen = new ReferencesScreen(Page);

        await Expect(screen.ClearedText).ToHaveTextAsync("4 of 11 cited sources cleared");
        await Expect(screen.ChipCount("risk")).ToHaveTextAsync("3");
        await Expect(screen.Gate).ToContainTextAsync("3 risks block print export");
        await Expect(screen.Rows).ToHaveCountAsync(12);
    }

    [Test, Order(2)]
    public async Task Badge_ShouldShowThreeInDangerOnReferencesAndOnPlan()
    {
        var bar = new AppShell(Page).BookBar;
        await Page.GotoAsync(Links.References(Book));

        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");
        await Expect(bar.ReferencesBadge).ToHaveAttributeAsync("data-tone", "danger");

        await Page.GotoAsync(Links.Plan(Book));

        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");
        await Expect(bar.ReferencesBadge).ToHaveAttributeAsync("data-tone", "danger");
    }

    [Test, Order(3)]
    public async Task NeedsCheckChip_ShouldFilter()
    {
        await Page.GotoAsync(Links.References(Book));
        var screen = new ReferencesScreen(Page);

        await screen.Chip("needs_check").ClickAsync();

        await Expect(screen.Rows).ToHaveCountAsync(4);
        await Expect(screen.RowStatuses.Filter(new() { HasNotText = "Needs check" })).ToHaveCountAsync(0);
        await Expect(screen.Chip("needs_check")).ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Test, Order(4)]
    public async Task Hash_WhenVelocityTrapId_ShouldSelectRow()
    {
        await Page.GotoAsync(Links.References(Book, Links.Hash((SeedIds.References.VelocityTrap.ToString(), null))));
        var screen = new ReferencesScreen(Page);

        await Expect(screen.Row(SeedIds.References.VelocityTrap)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(screen.Row(SeedIds.References.VelocityTrap)).ToContainTextAsync("Quoted prose: 342 of 300 words");
        await Expect(screen.Row(SeedIds.References.BeckXp)).ToHaveAttributeAsync("aria-selected", "false");
    }

    [Test, Order(5)]
    public async Task Row_WhenClicked_ShouldPutItsIdInTheHash()
    {
        await Page.GotoAsync(Links.References(Book));
        var screen = new ReferencesScreen(Page);

        await screen.Row(SeedIds.References.Accelerate).ClickAsync();

        await Expect(screen.Row(SeedIds.References.Accelerate)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"#{SeedIds.References.Accelerate}$"));
    }
}
