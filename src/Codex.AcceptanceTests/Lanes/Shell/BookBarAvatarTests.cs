using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Shell;

// The book bar ends with the seeded user's avatar, as in the mockups, and has no ⌘K key cap.
[TestFixture]
public class BookBarAvatarTests : SeededAcceptanceTest
{
    [Test]
    public async Task BookBar_OnABookScreen_ShouldEndWithTheUsersInitials()
    {
        var bar = new BookBar(Page);

        await Page.GotoAsync(Links.Write(SeedIds.Books.ProductEngineering.ToString()));

        await Expect(bar.Avatar).ToHaveTextAsync("JD");
        await Expect(bar.Avatar).ToHaveAttributeAsync("title", "Joel Dickson");
        await Expect(bar.PaletteButton).ToHaveCountAsync(0);
    }
}
