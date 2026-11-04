using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Features.Auth;

namespace Codex.WebApi.Tests.Auth;

// Sign-in is limited to 30 attempts a minute per address (SignInRateLimiter), counted before the per-email checks: the
// 31st attempt from one IP is refused with 429 rate_limited, whichever email it names.
[TestFixture]
public class SignInRateLimitTests : ApiFixture
{
    protected override TestUser SignedInAs => TestUser.Anonymous;

    [Test]
    public async Task Login_WhenTheThirtyFirstAttemptInAMinuteComesFromOneAddress_ShouldBeRateLimited()
    {
        using var client = AuthClient.Create(Host);
        for (var i = 0; i < SignInRateLimiter.PerMinutePerIp; i++)
        {
            (await client.LoginAsync($"nobody{i}@example.test", "wrong-password")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var refused = await client.LoginAsync("nobody-else@example.test", "wrong-password");

        (await refused.ReadApiErrorAsync(HttpStatusCode.TooManyRequests)).Code.ShouldBe(ApiErrorCodes.RateLimited);
    }

    [Test]
    public async Task Login_WhenAnotherAddressIsNamedByCloudflare_ShouldBeCountedApart()
    {
        using var client = AuthClient.Create(Host);
        for (var i = 0; i < SignInRateLimiter.PerMinutePerIp; i++)
        {
            using var request = Attempt($"flooder{i}@example.test", "203.0.113.7");
            (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var flooder = Attempt("flooder@example.test", "203.0.113.7");
        using var other = Attempt("other@example.test", "203.0.113.8");

        (await client.SendAsync(flooder)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await client.SendAsync(other)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage Attempt(string email, string address)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new Codex.Contracts.Auth.LoginRequest(email, "wrong-password")),
        };
        request.Headers.Add("CF-Connecting-IP", address);
        return request;
    }
}
