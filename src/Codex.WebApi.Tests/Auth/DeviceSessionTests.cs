using System.Net;
using System.Net.Http.Json;
using Codex.Application.Identity;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.Core;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// The mobile app's device sessions (#35 M1): sign in with email, password and a device name; the secret then signs
// /api requests with no cookie. The list marks the current device, a session can be revoked from another device or
// ended by the device itself, and neither a personal access token on /api nor a device secret on /mcp is accepted.
[TestFixture]
public class DeviceSessionTests : ApiFixture
{
    private const string Email = "phone@example.test";
    private const string Password = "a-long-password";
    private Guid _userId;
    private CreatedDeviceSessionDto _phone = null!;
    private CreatedDeviceSessionDto _tablet = null!;

    protected override TestUser SignedInAs => TestUser.Anonymous;

    [OneTimeSetUp]
    public async Task CreateUserAsync() => _userId = await TestUsers.CreateAsync(ConnectionString, Email, "Phone User", Password);

    [Test]
    [Order(1)]
    public async Task SignIn_ShouldReturnTheSecretOnceAndSetNoCookie()
    {
        using var client = AuthClient.Create(Host);

        var response = await client.SignInAsync(" PHONE@example.test ", Password, " Phone's iPhone ");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        _phone = (await response.Content.ReadFromJsonAsync<CreatedDeviceSessionDto>())!;
        _phone.Secret.ShouldMatch("^cdx_dev_[0-9A-Za-z]{43}$");
        _phone.Session.Prefix.ShouldBe(_phone.Secret[..16]);
        _phone.Session.Name.ShouldBe("Phone's iPhone");
        _phone.Session.Current.ShouldBeTrue();
        (_phone.Session.ExpiresAt - _phone.Session.CreatedAt).ShouldBe(TimeSpan.FromDays(90), TimeSpan.FromSeconds(5));
        _phone.Session.ExpiresLabel.ShouldBe(DisplayDate.ShortDay(_phone.Session.ExpiresAt, TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone));
        _phone.Session.LastUsedLabel.ShouldBeNull();
        _phone.Session.CreatedLabel.ShouldBe("just now");
        _phone.Me.Id.ShouldBe(_userId);
        _phone.Me.Email.ShouldBe(Email);
    }

    [Test]
    [Order(2)]
    public async Task Api_WhenCalledWithTheSecretAndNoCookie_ShouldActAsTheUser()
    {
        using var client = DeviceClient.WithBearer(Host, _phone.Secret);

        var me = await client.GetAsync("/api/auth/me");
        var library = await client.GetAsync("/api/library");

        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<MeDto>())!.Id.ShouldBe(_userId);
        library.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestCase("", "Give the device a name, such as \"JD's iPhone\".")]
    [TestCase("   ", "Give the device a name, such as \"JD's iPhone\".")]
    [TestCase(null, "Give the device a name, such as \"JD's iPhone\".")]
    [Order(3)]
    public async Task SignIn_WhenTheDeviceNameIsMissing_ShouldBe400(string? name, string message)
    {
        using var client = AuthClient.Create(Host);

        var response = await client.SignInAsync(Email, Password, name!);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).ShouldBe(new ApiError(message, ApiErrorCodes.Validation));
    }

    [Test]
    [Order(4)]
    public async Task SignIn_WhenTheDeviceNameIsTooLong_ShouldBe400()
    {
        using var client = AuthClient.Create(Host);

        var response = await client.SignInAsync(Email, Password, new string('x', DeviceSessions.MaxName + 1));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Error.ShouldBe("The name can have at most 100 characters.");
    }

    [Test]
    [Order(5)]
    public async Task List_ShouldMarkTheSessionTheRequestCameWithAsCurrent()
    {
        using var anon = AuthClient.Create(Host);
        _tablet = await DeviceClient.CreateSessionAsync(anon, Email, Password, "Tablet");
        using var phone = DeviceClient.WithBearer(Host, _phone.Secret);

        var sessions = (await phone.GetFromJsonAsync<List<DeviceSessionDto>>("/api/auth/device-sessions"))!;

        sessions.Select(s => (s.Name, s.Current)).ShouldBe([("Tablet", false), ("Phone's iPhone", true)]);
        sessions[1].LastUsedLabel.ShouldBe("just now");
        (await phone.GetStringAsync("/api/auth/device-sessions")).ShouldNotContain(_phone.Secret);
    }

    [Test]
    [Order(6)]
    public async Task Revoke_WhenTheSessionIsAnotherUsers_ShouldBe404()
    {
        var otherId = await TestUsers.CreateAsync(ConnectionString, "other@example.test", "Other", "other-password");
        using var anon = AuthClient.Create(Host);
        var others = await DeviceClient.CreateSessionAsync(anon, "other@example.test", "other-password", "Their phone");
        using var phone = DeviceClient.WithBearer(Host, _phone.Secret);

        var response = await phone.DeleteAsync($"/api/auth/device-sessions/{others.Session.Id}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        (await phone.DeleteAsync($"/api/auth/device-sessions/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var theirs = DeviceClient.WithBearer(Host, others.Secret);
        (await theirs.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await theirs.GetFromJsonAsync<MeDto>("/api/auth/me"))!.Id.ShouldBe(otherId);
    }

    [Test]
    [Order(7)]
    public async Task Revoke_ShouldStopTheOtherDevicesSecretAndDropItFromTheList()
    {
        using var phone = DeviceClient.WithBearer(Host, _phone.Secret);

        var response = await phone.DeleteAsync($"/api/auth/device-sessions/{_tablet.Session.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var tablet = DeviceClient.WithBearer(Host, _tablet.Secret);
        (await (await tablet.GetAsync("/api/auth/me")).ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Code.ShouldBe(ApiErrorCodes.Unauthorized);
        var sessions = (await phone.GetFromJsonAsync<List<DeviceSessionDto>>("/api/auth/device-sessions"))!;
        sessions.Select(s => s.Name).ShouldBe(["Phone's iPhone"]);
    }

    [Test]
    [Order(8)]
    public async Task Api_WhenCalledWithAPersonalAccessToken_ShouldBe401()
    {
        var token = await TestTokens.CreateAsync(ConnectionString, _userId, "Agent", [ApiTokenScopes.Read]);
        using var client = DeviceClient.WithBearer(Host, token.Secret);

        var response = await client.GetAsync("/api/library");

        (await response.ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Code.ShouldBe(ApiErrorCodes.Unauthorized);
    }

    [Test]
    [Order(9)]
    public async Task Mcp_WhenCalledWithADeviceSecret_ShouldBe401()
    {
        using var client = DeviceClient.WithBearer(Host, _phone.Secret);

        var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });

        (await response.ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Code.ShouldBe(ApiErrorCodes.Unauthorized);
    }

    [Test]
    [Order(10)]
    public async Task Api_WhenTheSecretIsMadeUp_ShouldBe401()
    {
        using var client = DeviceClient.WithBearer(Host, DeviceSessions.SecretPrefix + new string('a', 43));

        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Order(11)]
    public async Task Login_ShouldStillSignInWithTheCookie()
    {
        using var browser = await AuthClient.SignedInAsync(Host, Email, Password);

        (await browser.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var sessions = (await browser.GetFromJsonAsync<List<DeviceSessionDto>>("/api/auth/device-sessions"))!;
        sessions.ShouldAllBe(s => !s.Current);
        (await (await browser.DeleteAsync("/api/auth/device-sessions/current")).ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(12)]
    public async Task RevokeCurrent_ShouldEndTheSessionTheRequestCameWith()
    {
        using var phone = DeviceClient.WithBearer(Host, _phone.Secret);

        var response = await phone.DeleteAsync("/api/auth/device-sessions/current");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await phone.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await phone.DeleteAsync("/api/auth/device-sessions/current")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
