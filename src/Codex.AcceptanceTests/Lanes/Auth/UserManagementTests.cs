using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Auth;
using Codex.Contracts;
using Codex.Contracts.Auth;

namespace Codex.AcceptanceTests.Lanes.Auth;

// /settings/users as JD, the seeded admin: add a user, see their one-time password once, disable them.
[TestFixture]
public partial class UserManagementTests : SeededAcceptanceTest
{
    private const string Email = "added@example.test";
    private string _added = null!;

    [Test]
    [Order(1)]
    public async Task AddUser_ShouldShowTheOneTimePasswordAndListTheUser()
    {
        await Page.GotoAsync(Links.Library());
        var menu = new UserMenu(Page);
        await menu.OpenAsync();
        await menu.Users.ClickAsync();

        var users = new UsersScreen(Page);
        await Expect(users.Root).ToBeVisibleAsync();
        await users.AddAsync(Email, "Added Writer");

        await Expect(users.OneTimePassword).ToHaveTextAsync(OneTimePassword());
        _added = await UserIdAsync(Email);
        await Expect(users.Status(_added)).ToHaveTextAsync("active · must change password");
    }

    [Test]
    [Order(2)]
    public async Task Disable_ShouldShowTheUserAsDisabled()
    {
        await Page.GotoAsync("/settings/users");
        var users = new UsersScreen(Page);

        await users.ToggleDisabled(_added).ClickAsync();

        await Expect(users.Status(_added)).ToHaveTextAsync("disabled");
        await Expect(users.ToggleDisabled(_added)).ToHaveTextAsync("Enable");
    }

    [Test]
    [Order(3)]
    public async Task ResetPassword_ShouldShowANewOneTimePassword()
    {
        await Page.GotoAsync("/settings/users");
        var users = new UsersScreen(Page);

        await users.ResetPassword(_added).ClickAsync();

        await Expect(users.OneTimePassword).ToHaveTextAsync(OneTimePassword());
    }

    [Test]
    [Order(4)]
    public async Task OwnRow_ShouldNotOfferToDisableOrDemoteYourself()
    {
        await Page.GotoAsync("/settings/users");
        var users = new UsersScreen(Page);
        var jd = SeedIds.Users.Jd.ToString();

        await Expect(users.ToggleAdmin(jd)).ToBeDisabledAsync();
        await Expect(users.ToggleDisabled(jd)).ToBeDisabledAsync();
    }

    private async Task<string> UserIdAsync(string email)
    {
        using var client = Host.CreateClient();
        var users = await client.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users");
        return users!.Single(u => u.Email == email).Id.ToString();
    }

    [GeneratedRegex("^[A-Za-z0-9]{16}$")]
    private static partial Regex OneTimePassword();
}
