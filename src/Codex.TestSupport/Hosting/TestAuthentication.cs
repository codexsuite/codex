using System.Security.Claims;
using System.Text.Encodings.Web;
using Codex.WebApi.Features.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codex.TestSupport.Hosting;

// Test sign-in, added through ConfigureTestServices (production has no switch for it). A request that carries a device
// session bearer (#35) goes to the device scheme, as in production. A request that carries the real
// sign-in cookie is authenticated by the cookie, as in production (the /login acceptance test, a disabled user's
// cookie); any other request is signed in as the host's TestUser, or as the user its X-Test-User header names
// ("anonymous" for none), so one API test can act as user A and user B. Challenges and forbids still go to the cookie
// scheme, so an anonymous page load is redirected to /login and an anonymous /api call gets 401, as in production.
public static class TestAuthentication
{
    public const string Scheme = "Test";
    public const string UserHeader = "X-Test-User";
    public const string AnonymousValue = "anonymous";

    private const string Selector = "TestOrCookie";

    public static void Add(IServiceCollection services, TestUser user)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = Selector;
                options.DefaultAuthenticateScheme = Selector;
                options.DefaultChallengeScheme = AuthServices.CookieScheme;
                options.DefaultForbidScheme = AuthServices.CookieScheme;
                options.DefaultSignInScheme = AuthServices.CookieScheme;
                options.DefaultSignOutScheme = AuthServices.CookieScheme;
            })
            .AddScheme<TestUserOptions, TestUserHandler>(Scheme, options => options.User = user)
            .AddPolicyScheme(Selector, Selector, options =>
            {
                options.ForwardDefaultSelector = context => Select(context.Request);
                options.ForwardChallenge = AuthServices.CookieScheme;
                options.ForwardForbid = AuthServices.CookieScheme;
                options.ForwardSignIn = AuthServices.CookieScheme;
                options.ForwardSignOut = AuthServices.CookieScheme;
            });
    }

    private static string Select(HttpRequest request)
    {
        if (DeviceBearer.Carries(request))
        {
            return AuthServices.DeviceScheme;
        }
        return request.Cookies.ContainsKey(AuthServices.CookieName) ? AuthServices.CookieScheme : Scheme;
    }

    // Sends this client's requests as userId (null: anonymous), whatever the host's default.
    public static HttpClient ActingAs(this HttpClient client, Guid? userId)
    {
        client.DefaultRequestHeaders.Remove(UserHeader);
        client.DefaultRequestHeaders.Add(UserHeader, userId?.ToString() ?? AnonymousValue);
        return client;
    }

    public sealed class TestUserOptions : AuthenticationSchemeOptions
    {
        public TestUser User { get; set; } = TestUser.Jd;
    }

    private sealed class TestUserHandler(IOptionsMonitor<TestUserOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<TestUserOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers[UserHeader].ToString();
            Guid? userId = header switch
            {
                "" => Options.User.Id,
                AnonymousValue => null,
                _ => Guid.Parse(header),
            };
            if (userId is not { } id)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
