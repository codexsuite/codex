using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.References;

// Removing the Velocity Trap leaves its two quotes in Ch 4 Sc 1 and Ch 8 Sc 1 as plain text.
[TestFixture]
public class ClearanceArchiveTests : ReferencesLaneTest
{
    private static Guid Source => SeedIds.References.VelocityTrap;

    [Test, Order(1)]
    public async Task Remove_WhenConfirmed_ShouldArchiveTheSourceAndLeavePlainText()
    {
        await Page.GotoAsync(SourceLink(Source));
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        var before4 = await ProseBodyAsync(SeedIds.Scenes.Ch4Sc1);
        before4.ShouldContain($"(ref:{SeedIds.ReferenceUses.VelocityTrapCh4Sc1})");
        var excerpt = await panel.UseExcerpt(SeedIds.ReferenceUses.VelocityTrapCh4Sc1).InnerTextAsync();

        await panel.Remove.ClickAsync();
        var response = await Page.RunAndWaitForApiAsync($"/api/references/{Source}/archive", "POST", () => panel.RemoveConfirm.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.Root).ToHaveCountAsync(0);
        await Expect(screen.Row(Source)).ToHaveCountAsync(0);
        await Expect(panel.Toast).ToContainTextAsync("plain text");
        var after4 = await ProseBodyAsync(SeedIds.Scenes.Ch4Sc1);
        var after8 = await ProseBodyAsync(SeedIds.Scenes.Ch8Sc1);
        after4.ShouldNotContain(SeedIds.ReferenceUses.VelocityTrapCh4Sc1.ToString());
        after8.ShouldNotContain(SeedIds.ReferenceUses.VelocityTrapCh8Sc1.ToString());
        after4.ShouldContain(excerpt.Split('\n')[0].Trim());
    }

    [Test, Order(2)]
    public async Task Remove_WhenTheSourceIsGone_ShouldDropTheRiskFromTheStrip()
    {
        await Page.GotoAsync(Links.References(Book));
        var screen = new ReferencesScreen(Page);

        await Expect(screen.Chip("risk")).ToContainTextAsync("2");
        await Expect(screen.Rows).ToHaveCountAsync(11);
    }

    [Test, Order(3)]
    public async Task Panel_WhenTheArchivedSourceIsOpenedByLink_ShouldSayItWasNotFound()
    {
        await Page.GotoAsync(SourceLink(Source));

        await Expect(Page.GetByTestId(ReferencesTestIds.PanelError)).ToBeVisibleAsync();
    }
}
