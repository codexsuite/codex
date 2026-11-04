using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Auth;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;
using Npgsql;

namespace Codex.WebApi.Tests.Auth;

// What ends a device session besides revoking it: a password change or an admin reset (the security stamp), a disabled
// user, and expiry. Each is the next request's 401.
[TestFixture]
public class DeviceSessionEndingTests : ApiFixture
{
    private const string Email = "ending@example.test";
    private const string Password = "a-long-password";
    private Guid _userId;
    private HttpClient _admin = null!;

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [OneTimeSetUp]
    public async Task CreateUserAsync()
    {
        _userId = await TestUsers.CreateAsync(ConnectionString, Email, "Ending", Password);
        _admin = Host.CreateClient().ActingAs(TestUser.Jd.Id);
    }

    [OneTimeTearDown]
    public void DisposeAdmin() => _admin.Dispose();

    [Test]
    [Order(1)]
    public async Task Request_WhenThePasswordWasChanged_ShouldEndOtherDevicesButNotTheCookieItChangedFrom()
    {
        using var anon = AuthClient.Create(Host);
        var phone = await DeviceClient.CreateSessionAsync(anon, Email, Password, "Phone");
        using var device = DeviceClient.WithBearer(Host, phone.Secret);
        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var browser = await AuthClient.SignedInAsync(Host, Email, Password);

        (await browser.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(Password, "second-password"))).EnsureSuccessStatusCode();

        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await browser.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    [Order(2)]
    public async Task Request_WhenAnAdminResetThePassword_ShouldBe401()
    {
        using var anon = AuthClient.Create(Host);
        var phone = await DeviceClient.CreateSessionAsync(anon, Email, "second-password", "Phone");
        using var device = DeviceClient.WithBearer(Host, phone.Secret);
        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _admin.PostAsync($"/api/admin/users/{_userId}/reset-password", null)).EnsureSuccessStatusCode();

        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Order(3)]
    public async Task Request_WhenTheUserWasDisabled_ShouldBe401AndWorkAgainWhenEnabled()
    {
        var oneTime = (await (await _admin.PostAsync($"/api/admin/users/{_userId}/reset-password", null))
            .Content.ReadFromJsonAsync<OneTimePasswordDto>())!.OneTimePassword;
        using var anon = AuthClient.Create(Host);
        var phone = await DeviceClient.CreateSessionAsync(anon, Email, oneTime, "Phone");
        using var device = DeviceClient.WithBearer(Host, phone.Secret);

        (await _admin.PatchAsJsonAsync($"/api/admin/users/{_userId}", new UpdateUserRequest(null, Disabled: true, null))).EnsureSuccessStatusCode();
        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await _admin.PatchAsJsonAsync($"/api/admin/users/{_userId}", new UpdateUserRequest(null, Disabled: false, null))).EnsureSuccessStatusCode();
        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    [Order(4)]
    public async Task Request_WhenTheSessionExpired_ShouldBe401AndLeaveTheList()
    {
        using var anon = AuthClient.Create(Host);
        var oneTime = (await (await _admin.PostAsync($"/api/admin/users/{_userId}/reset-password", null))
            .Content.ReadFromJsonAsync<OneTimePasswordDto>())!.OneTimePassword;
        var phone = await DeviceClient.CreateSessionAsync(anon, Email, oneTime, "Phone");
        using var device = DeviceClient.WithBearer(Host, phone.Secret);
        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var browser = await AuthClient.SignedInAsync(Host, Email, oneTime);

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE device_session SET expires_at = now() - interval '1 second' WHERE id = @id", new { id = phone.Session.Id });
        }

        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var sessions = (await browser.GetFromJsonAsync<List<DeviceSessionDto>>("/api/auth/device-sessions"))!;
        sessions.ShouldNotContain(s => s.Id == phone.Session.Id);
    }

    [Test]
    [Order(5)]
    public async Task Request_ShouldSlideTheExpiryNinetyDaysOnFromTheLastUse()
    {
        using var anon = AuthClient.Create(Host);
        var oneTime = (await (await _admin.PostAsync($"/api/admin/users/{_userId}/reset-password", null))
            .Content.ReadFromJsonAsync<OneTimePasswordDto>())!.OneTimePassword;
        var phone = await DeviceClient.CreateSessionAsync(anon, Email, oneTime, "Phone");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE device_session SET expires_at = now() + interval '1 day', last_used_at = now() - interval '2 minutes' WHERE id = @id",
            new { id = phone.Session.Id });
        using var device = DeviceClient.WithBearer(Host, phone.Secret);

        (await device.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var days = await connection.ExecuteScalarAsync<double>(
            "SELECT extract(epoch FROM expires_at - now()) / 86400 FROM device_session WHERE id = @id", new { id = phone.Session.Id });
        days.ShouldBe(90, 0.01);
    }
}
