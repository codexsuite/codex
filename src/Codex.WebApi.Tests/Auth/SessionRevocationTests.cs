using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Auth;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// A session is checked against app_user on each request: disabling the user, resetting their password or their own
// password change ends the cookies issued before it, on the very next request.
[TestFixture]
public class SessionRevocationTests : ApiFixture
{
    private const string Email = "writer@example.test";
    private const string Password = "first-password";
    private Guid _userId;

    protected override TestUser SignedInAs => TestUser.Anonymous;

    private HttpClient Admin { get; set; } = null!;

    [OneTimeSetUp]
    public async Task CreateUserAsync()
    {
        _userId = await TestUsers.CreateAsync(ConnectionString, Email, "Writer", Password);
        Admin = Host.CreateClient().ActingAs(TestUser.Jd.Id);
    }

    [OneTimeTearDown]
    public void DisposeAdmin() => Admin.Dispose();

    [Test]
    [Order(1)]
    public async Task Request_WhenTheUserWasDisabledSinceSigningIn_ShouldBe401()
    {
        using var writer = await AuthClient.SignedInAsync(Host, Email, Password);
        (await writer.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await Admin.PatchAsJsonAsync($"/api/admin/users/{_userId}", new UpdateUserRequest(null, Disabled: true, null))).EnsureSuccessStatusCode();

        (await writer.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthClient.Create(Host).LoginAsync(Email, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Order(2)]
    public async Task ResetPassword_ShouldEndTheOldSessionAndRequireANewPasswordAtSignIn()
    {
        (await Admin.PatchAsJsonAsync($"/api/admin/users/{_userId}", new UpdateUserRequest(null, Disabled: false, null))).EnsureSuccessStatusCode();
        using var before = await AuthClient.SignedInAsync(Host, Email, Password);

        var reset = await (await Admin.PostAsync($"/api/admin/users/{_userId}/reset-password", null)).Content.ReadFromJsonAsync<OneTimePasswordDto>();

        (await before.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthClient.Create(Host).LoginAsync(Email, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var after = await AuthClient.SignedInAsync(Host, Email, reset!.OneTimePassword);
        (await after.GetFromJsonAsync<MeDto>("/api/auth/me"))!.MustChangePassword.ShouldBeTrue();
        reset.User.MustChangePassword.ShouldBeTrue();
    }

    [Test]
    [Order(3)]
    public async Task ChangePassword_ShouldKeepThisSessionAndEndEveryOther()
    {
        var oneTime = (await (await Admin.PostAsync($"/api/admin/users/{_userId}/reset-password", null))
            .Content.ReadFromJsonAsync<OneTimePasswordDto>())!.OneTimePassword;
        using var other = await AuthClient.SignedInAsync(Host, Email, oneTime);
        using var mine = await AuthClient.SignedInAsync(Host, Email, oneTime);

        var changed = await mine.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(oneTime, "my-own-password"));

        (await changed.Content.ReadFromJsonAsync<MeDto>())!.MustChangePassword.ShouldBeFalse();
        (await mine.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthClient.Create(Host).LoginAsync(Email, "my-own-password")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
