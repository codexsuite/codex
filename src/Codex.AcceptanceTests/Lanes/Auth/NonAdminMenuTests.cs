using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Auth;
using Codex.TestSupport.Hosting;

namespace Codex.AcceptanceTests.Lanes.Auth;

// A user who is not an admin has no Users item, and an empty library of their own.
[TestFixture]
public class NonAdminMenuTests : SeededAcceptanceTest
{
    private static readonly Guid Writer = new("0199c0de-0000-7000-8000-0000000000c1");

    protected override TestUser SignedInAs => TestUser.Of(Writer);

    [OneTimeSetUp]
    public Task CreateWriterAsync()
        => TestUsers.CreateAsync(ConnectionString, "writer@example.test", "Wendy Writer", id: Writer);

    [Test]
    public async Task Menu_WhenNotAnAdmin_ShouldOfferAccountAndSignOutOnly()
    {
        await Page.GotoAsync(Links.Library());
        var menu = new UserMenu(Page);
        await Expect(menu.Button).ToHaveTextAsync("WW");

        await menu.OpenAsync();

        await Expect(menu.Account).ToBeVisibleAsync();
        await Expect(menu.SignOut).ToBeVisibleAsync();
        await Expect(menu.Users).ToHaveCountAsync(0);
    }
}
