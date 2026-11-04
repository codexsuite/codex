using System.Net;
using System.Net.Http.Json;
using Codex.Application.Identity;
using Codex.Contracts.Auth;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// Ten wrong passwords in a row lock the account: even the right password is refused until an admin unlocks it.
[TestFixture]
public class SignInLockoutTests : ApiFixture
{
    private const string Email = "locked@example.test";
    private const string Password = "a-long-password";
    private Guid _userId;

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [OneTimeSetUp]
    public async Task CreateUserAsync() => _userId = await TestUsers.CreateAsync(ConnectionString, Email, "Locked Out", Password);

    [Test]
    [Order(1)]
    public async Task Login_AfterTenWrongPasswords_ShouldRefuseTheRightOneWithTheSameMessage()
    {
        using var client = AuthClient.Create(Host);
        for (var i = 0; i < SignInService.LockoutFailures; i++)
        {
            (await client.LoginAsync(Email, "wrong-password")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var response = await client.LoginAsync(Email, Password);

        (await response.ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Error.ShouldBe(SignInService.Refusal);
    }

    [Test]
    [Order(2)]
    public async Task Users_WhenTheAccountIsLocked_ShouldShowLockedUntilAnAdminUnlocksIt()
    {
        using var admin = Host.CreateClient().ActingAs(TestUser.Jd.Id);
        (await admin.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users"))!.Single(u => u.Id == _userId).Status.ShouldBe("locked");

        var unlocked = await admin.PatchAsJsonAsync($"/api/admin/users/{_userId}", new UpdateUserRequest(null, null, Unlock: true));

        (await unlocked.Content.ReadFromJsonAsync<AdminUserDto>())!.Status.ShouldBe("active");
        using var client = AuthClient.Create(Host);
        (await client.LoginAsync(Email, Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
