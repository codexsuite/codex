using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Write;

[TestFixture]
public class WriteFocusTests : WriteLaneTest
{
    [Test, Order(1)]
    public async Task Focus_WhenEscPressed_ShouldLeave()
    {
        var write = new WritePageObject(Page);
        var sidebar = new KbSidebarFrame(Page);
        await Page.GotoAsync(Links.Write(SeedIds.Books.ProductEngineering.ToString()));
        await Expect(sidebar.Root).ToBeVisibleAsync();

        await write.FocusButton.ClickAsync();

        await Expect(write.FocusButton).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(sidebar.Root).ToBeHiddenAsync();
        await Expect(write.Minimap).ToBeHiddenAsync();
        await Expect(write.SceneHeading(SeedIds.Scenes.Ch5Sc1)).ToBeHiddenAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(write.FocusButton).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(sidebar.Root).ToBeVisibleAsync();
        await Expect(write.Minimap).ToBeVisibleAsync();
        await Expect(write.Manuscript).ToBeFocusedAsync();
    }
}
