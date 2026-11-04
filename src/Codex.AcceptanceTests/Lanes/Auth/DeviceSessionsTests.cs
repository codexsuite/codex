using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Auth;
using Codex.Contracts.Auth;

namespace Codex.AcceptanceTests.Lanes.Auth;

// /account → Signed-in devices as JD (#35): a phone signs in with a device session; it is listed, Sign out revokes it,
// and its secret then gets 401.
[TestFixture]
public class DeviceSessionsTests : SeededAcceptanceTest
{
    private const string DeviceName = "Test phone";

    [Test]
    public async Task SignOut_ShouldRevokeTheDeviceAndRejectItsSecret()
    {
        using var client = Host.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/device-sessions",
            new CreateDeviceSessionRequest("jd@codex.local", "codex-dev-password", DeviceName));
        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CreatedDeviceSessionDto>())!;
        var sessionId = created.Session.Id.ToString();

        await Page.GotoAsync("/account");
        var devices = new DeviceSessionsSection(Page);
        await Expect(devices.Row(sessionId)).ToContainTextAsync(DeviceName);
        await Expect(devices.Row(sessionId)).ToContainTextAsync(created.Session.Prefix);

        await devices.SignOut(sessionId).ClickAsync();

        await Expect(devices.Row(sessionId)).ToHaveCountAsync(0);
        await Expect(devices.Empty).ToBeVisibleAsync();
        using var device = Host.CreateClient();
        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Secret);
        (await device.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
