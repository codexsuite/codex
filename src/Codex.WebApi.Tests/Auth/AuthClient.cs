using System.Net.Http.Json;
using Codex.Contracts.Auth;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// A browser stand-in for the auth tests: its own cookie jar, no redirects followed, the X-Codex-Client header the SPA
// sends (CrossSiteWriteGuard), and sign-in through the real /api/auth/login (so its requests are authenticated by the
// cookie, not the test handler).
public static class AuthClient
{
    public const string ClientHeader = "X-Codex-Client";

    public static HttpClient Create(CodexHost host)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(host.BaseUrl) };
        client.DefaultRequestHeaders.Add(ClientHeader, "auth-tests");
        return client;
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    public static async Task<HttpClient> SignedInAsync(CodexHost host, string email, string password)
    {
        var client = Create(host);
        var response = await client.LoginAsync(email, password);
        response.EnsureSuccessStatusCode();
        return client;
    }
}
