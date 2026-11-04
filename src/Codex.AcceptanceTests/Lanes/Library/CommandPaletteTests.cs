using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.Contracts;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Lanes.Library;

// From the seed: the palette lists books everywhere, Enter opens one, Esc closes.
[TestFixture]
public class CommandPaletteTests : SeededAcceptanceTest
{
    private CommandPalette Palette => new(Page);

    private async Task OpenPaletteAsync(string url)
    {
        await Page.GotoAsync(url);
        // The shortcut only works once the app has started: the Library bar draws its key cap by then, and a book
        // screen (whose bar has no key cap) its book bar.
        await Expect(url == Links.Library() ? Palette.Button : Page.GetByTestId(TestIds.Shell.BookBar)).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Control+k");
        await Expect(Palette.Root).ToBeVisibleAsync();
    }

    [Test, Order(1)]
    public async Task Palette_WhenEnterOnBook_ShouldOpenIt()
    {
        await OpenPaletteAsync(Links.Library());
        await Expect(Palette.Input).ToBeFocusedAsync();

        await Palette.Input.FillAsync("Northbound");
        await Expect(Palette.Book(SeedIds.Books.Northbound.ToString())).ToHaveAttributeAsync("aria-selected", "true");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{SeedIds.Books.Northbound}/(plan|write|references|layout|settings)$"));
        await Expect(Palette.Root).ToBeHiddenAsync();
    }

    [Test, Order(2)]
    public async Task Palette_WhenEsc_ShouldClose()
    {
        await OpenPaletteAsync(Links.Library());

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Palette.Root).ToBeHiddenAsync();
    }

    [Test, Order(3)]
    public async Task Palette_WhenOpenedOnBookScreen_ShouldListBooks()
    {
        await OpenPaletteAsync(Links.Write(SeedIds.Books.ProductEngineering.ToString()));

        await Expect(Palette.Book(SeedIds.Books.Northbound.ToString())).ToBeVisibleAsync();
        await Expect(Palette.Book(SeedIds.Books.PlayersGuide.ToString())).ToBeVisibleAsync();
        await Expect(Palette.Series(SeedIds.Series.ForsakenGods.ToString())).ToBeVisibleAsync();
        await Expect(Palette.Action("new-book")).ToBeVisibleAsync();
    }
}
