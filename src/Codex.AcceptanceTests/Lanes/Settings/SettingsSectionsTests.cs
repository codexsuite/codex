using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Settings;

// The section nav and deep links on Product Engineering's Settings.
[TestFixture]
public class SettingsSectionsTests : SeededAcceptanceTest
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();

    private SettingsScreen Settings => new(Page);

    [Test, Order(1)]
    public async Task Nav_WhenSectionClicked_ShouldSetTheHashAndShowTheCard()
    {
        await Page.GotoAsync(Links.Settings(Book));
        await Expect(Settings.Title).ToHaveValueAsync("Product Engineering");

        await Settings.NavItem("writing").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("#writing$"));
        await Expect(Settings.NavItem("writing")).ToHaveAttributeAsync("aria-current", "true");
        await Expect(Settings.Card("writing")).ToBeInViewportAsync();
    }

    [Test, Order(2)]
    public async Task DeepLink_WhenSectionHash_ShouldScrollToIt()
    {
        await Page.GotoAsync(Links.Settings(Book, Links.Hash(("archive", null))));

        await Expect(Settings.Card("archive")).ToBeInViewportAsync();
        await Expect(Settings.NavItem("archive")).ToHaveAttributeAsync("aria-current", "true");
    }

    [Test, Order(3)]
    public async Task Title_WhenEdited_ShouldSaveAndShowInTheBookBar()
    {
        await Page.GotoAsync(Links.Settings(Book));
        await Settings.Title.FillAsync("Product Engineering, Second Edition");

        var saved = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/settings", "PATCH",
            () => Settings.Title.BlurAsync());

        saved.Status.ShouldBe(200);
        await Expect(Page.GetByTestId("shell-book-title")).ToContainTextAsync("Second Edition");
    }
}
