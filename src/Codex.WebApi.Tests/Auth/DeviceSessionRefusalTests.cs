using System.Net;
using Codex.Application.Identity;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// A refused device sign-in is the same 401 and sentence as /login's, and counts toward the same lockout.
[TestFixture]
public class DeviceSessionRefusalTests : ApiFixture
{
    private const string Email = "refused@example.test";
    private const string Password = "a-long-password";

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [OneTimeSetUp]
    public Task CreateUserAsync() => TestUsers.CreateAsync(ConnectionString, Email, "Refused", Password);

    [Test]
    [Order(1)]
    public async Task SignIn_WhenThePasswordIsWrong_ShouldBeTheSame401AsLogin()
    {
        using var client = AuthClient.Create(Host);

        var device = await client.SignInAsync(Email, "wrong-password", "Phone");
        var login = await client.LoginAsync(Email, "wrong-password");

        var deviceError = await device.ReadApiErrorAsync(HttpStatusCode.Unauthorized);
        deviceError.Error.ShouldBe(SignInService.Refusal);
        deviceError.ShouldBe(await login.ReadApiErrorAsync(HttpStatusCode.Unauthorized));
    }

    [Test]
    [Order(2)]
    public async Task SignIn_AfterTenWrongPasswords_ShouldRefuseTheRightOneOnBothRoutes()
    {
        using var client = AuthClient.Create(Host);
        for (var i = 0; i < SignInService.LockoutFailures - 1; i++)
        {
            (await client.SignInAsync(Email, "wrong-password", "Phone")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var device = await client.SignInAsync(Email, Password, "Phone");
        var login = await client.LoginAsync(Email, Password);

        (await device.ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Error.ShouldBe(SignInService.Refusal);
        (await login.ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Error.ShouldBe(SignInService.Refusal);
    }
}
