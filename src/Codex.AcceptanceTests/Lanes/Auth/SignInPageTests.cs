using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Auth;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.TestSupport.Hosting;

namespace Codex.AcceptanceTests.Lanes.Auth;

// The real /login page with the seeded dev password (no test sign-in: this browser has only the cookie it earns).
[TestFixture]
public class SignInPageTests : SeededAcceptanceTest
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [Test]
    [Order(1)]
    public async Task PageLoad_WhenSignedOut_ShouldLandOnSignInAndComeBackAfterIt()
    {
        await Page.GotoAsync(Links.Plan(Book));

        await Expect(Page).ToHaveURLAsync(SignInReturningTo());
        var login = new LoginScreen(Page);
        await login.SignInAsync(TestUsers.JdEmail, "not-the-password");
        await Expect(login.Error).ToContainTextAsync("Incorrect email or password");

        await login.SignInAsync(TestUsers.JdEmail, TestUsers.DevPassword);

        await Expect(new BookBar(Page).Root).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{Book}/plan$"));
        var menu = new UserMenu(Page);
        await Expect(menu.Button).ToHaveTextAsync("JD");
    }

    [Test]
    [Order(2)]
    public async Task SignOut_ShouldLandOnSignInAndKeepTheAppClosed()
    {
        await Page.GotoAsync(Links.Library());
        var menu = new UserMenu(Page);
        await menu.OpenAsync();
        await Expect(menu.Name).ToHaveTextAsync("Joel Dickson");

        await menu.SignOut.ClickAsync();

        await Expect(new LoginScreen(Page).Root).ToBeVisibleAsync();
        await Page.GotoAsync(Links.Library());
        await Expect(new LoginScreen(Page).Root).ToBeVisibleAsync();
    }

    [Test]
    [Order(3)]
    public async Task SignIn_WhenTheReturnUrlIsAnotherSite_ShouldOpenTheLibraryInstead()
    {
        await Page.GotoAsync("/login?returnUrl=https%3A%2F%2Fevil.example%2F");

        await new LoginScreen(Page).SignInAsync(TestUsers.JdEmail, TestUsers.DevPassword);

        await Expect(new LibraryShelf(Page).Root).ToBeVisibleAsync();
        new Uri(Page.Url).Host.ShouldBe("127.0.0.1");
        new Uri(Page.Url).AbsolutePath.ShouldBe("/");
    }

    private static Regex SignInReturningTo() => new($"/login\\?returnUrl=%2Fbooks%2F{Book}%2Fplan$");
}
