using System.Net.Http.Headers;
using System.Net.Http.Json;
using Codex.Contracts.Auth;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// The mobile app's stand-in for the device-session tests: no cookie jar use, a bearer secret on every request, and the
// X-Codex-Client header the app sends. The host is signed in as nobody, so only the bearer can authenticate.
public static class DeviceClient
{
    public static Task<HttpResponseMessage> SignInAsync(this HttpClient client, string email, string password, string deviceName)
        => client.PostAsJsonAsync("/api/auth/device-sessions", new CreateDeviceSessionRequest(email, password, deviceName));

    public static async Task<CreatedDeviceSessionDto> CreateSessionAsync(HttpClient client, string email, string password, string deviceName)
    {
        var response = await client.SignInAsync(email, password, deviceName);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedDeviceSessionDto>())!;
    }

    public static HttpClient WithBearer(CodexHost host, string secret)
    {
        var client = AuthClient.Create(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return client;
    }
}
