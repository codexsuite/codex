using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Auth;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts.Auth;
using Codex.TestSupport.Hosting;

namespace Codex.AcceptanceTests.Lanes.Auth;

// After an admin resets a password, the one-time password signs in but nothing else loads until it is replaced.
[TestFixture]
public class MustChangePasswordTests : SeededAcceptanceTest
{
    private const string Email = "reset@example.test";
    private string _oneTime = null!;

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [OneTimeSetUp]
    public async Task ResetAUsersPasswordAsync()
    {
        var userId = await TestUsers.CreateAsync(ConnectionString, Email, "Reset Writer", "their-old-password");
        using var admin = Host.CreateClient().ActingAs(TestUser.Jd.Id);
        var reset = await admin.PostAsync($"/api/admin/users/{userId}/reset-password", null);
        var created = await reset.Content.ReadFromJsonAsync<OneTimePasswordDto>();
        _oneTime = created?.OneTimePassword ?? throw new InvalidOperationException("The reset returned no password.");
    }

    [Test]
    public async Task SignIn_WithAOneTimePassword_ShouldHoldEveryRouteOnChangePasswordUntilItIsChanged()
    {
        await Page.GotoAsync(Links.Library());
        await new LoginScreen(Page).SignInAsync(Email, _oneTime);

        var password = new PasswordScreen(Page);
        await Expect(password.MustChange).ToBeVisibleAsync();
        await Page.GotoAsync(Links.Library());
        await Expect(password.MustChange).ToBeVisibleAsync();
        await Expect(new LibraryShelf(Page).Root).ToHaveCountAsync(0);

        await password.ChangeAsync(_oneTime, "my-own-new-password", "a-different-password");
        await Expect(password.Error).ToHaveTextAsync("The new passwords do not match.");
        await password.ChangeAsync(_oneTime, "my-own-new-password");

        await Expect(new LibraryShelf(Page).Root).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Expect(new LibraryShelf(Page).Root).ToBeVisibleAsync();
    }
}
