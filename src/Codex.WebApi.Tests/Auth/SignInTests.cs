using System.Net;
using System.Net.Http.Json;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Auth;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// Sign-in with no test user: requests are anonymous until /api/auth/login sets the cookie.
[TestFixture]
public class SignInTests : ApiFixture
{
    protected override TestUser SignedInAs => TestUser.Anonymous;

    [Test]
    public async Task Login_WithTheSeededDevPassword_ShouldSetAnHttpOnlyLaxCookieAndAnswerMe()
    {
        using var client = AuthClient.Create(Host);

        var response = await client.LoginAsync("JD@codex.local", TestUsers.DevPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("codex.auth=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldNotContain("secure", Case.Insensitive);
        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        me.ShouldBe(new MeDto(SeedIds.Users.Jd, TestUsers.JdEmail, "Joel Dickson", "JD", IsAdmin: true, MustChangePassword: false));
        (await client.GetAsync("/api/library")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Login_WhenTheRequestCameOverHttpsThroughTheProxy_ShouldMarkTheCookieSecure()
    {
        using var client = AuthClient.Create(Host);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(TestUsers.JdEmail, TestUsers.DevPassword)),
        };
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("codex.auth=", StringComparison.Ordinal))
            .ShouldContain("secure", Case.Insensitive);
    }

    [TestCase(TestUsers.JdEmail, "not-the-password")]
    [TestCase("nobody@example.test", TestUsers.DevPassword)]
    [TestCase("", "")]
    public async Task Login_WhenTheEmailOrPasswordIsWrong_ShouldAnswer401WithTheOneGenericMessage(string email, string password)
    {
        using var client = AuthClient.Create(Host);

        var response = await client.LoginAsync(email, password);

        var error = await response.ReadApiErrorAsync(HttpStatusCode.Unauthorized);
        error.ShouldBe(new ApiError(SignInService.Refusal, ApiErrorCodes.Unauthorized));
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [TestCase("/api/library")]
    [TestCase("/api/auth/me")]
    public async Task Api_WhenAnonymous_ShouldAnswer401(string path)
    {
        using var client = AuthClient.Create(Host);

        var response = await client.GetAsync(path);

        (await response.ReadApiErrorAsync(HttpStatusCode.Unauthorized)).Code.ShouldBe(ApiErrorCodes.Unauthorized);
    }

    [Test]
    public async Task Api_WhenAnonymousAndTheBookIsUnknown_ShouldStill401RatherThan404()
    {
        using var client = AuthClient.Create(Host);

        var response = await client.GetAsync($"/api/books/{SeedIds.Books.ProductEngineering}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PageLoad_WhenAnonymous_ShouldRedirectToTheSignInPageWithAReturnUrl()
    {
        using var client = AuthClient.Create(Host);

        var response = await client.GetAsync("/books/abc/plan?view=board");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.AbsolutePath.ShouldBe("/login");
        Uri.UnescapeDataString(location.Query).ShouldBe("?returnUrl=/books/abc/plan?view=board");
    }

    [TestCase("/health")]
    [TestCase("/api/health")]
    public async Task Health_WhenAnonymous_ShouldAnswer(string path)
    {
        using var client = AuthClient.Create(Host);

        (await client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Logout_WhenSignedIn_ShouldEndTheSession()
    {
        using var client = await AuthClient.SignedInAsync(Host, TestUsers.JdEmail, TestUsers.DevPassword);

        (await client.PostAsync("/api/auth/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
