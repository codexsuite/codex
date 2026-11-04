using Codex.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

namespace Codex.WebApi.Features.Auth;

// Email-and-password sign-in (#29). A Secure (over https), HttpOnly, SameSite=Lax cookie with a 14-day sliding expiry;
// the browser never holds a token. Every endpoint requires a signed-in user unless it opts out (AllowAnonymous: the
// sign-in page and endpoint, /health, the localhost MCP binding); static assets are served before authorization runs.
// /api, /hubs and /mcp answer 401/403 instead of redirecting (CookieSessionEvents).
public sealed class AuthServices : IServiceModule
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string CookieName = "codex.auth";
    public const string DeviceScheme = "DeviceBearer";
    public const string DeviceOrCookieScheme = "DeviceOrCookie";
    public const string AdminPolicy = "Admin";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Device sessions (#35): a request with a cdx_dev_ bearer on /api or /hubs goes to the device scheme, every other
        // request to the cookie. Challenge, forbid and sign-in/out stay with the cookie, so /api answers 401/403 as before.
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = DeviceOrCookieScheme;
                options.DefaultAuthenticateScheme = DeviceOrCookieScheme;
                options.DefaultChallengeScheme = CookieScheme;
                options.DefaultForbidScheme = CookieScheme;
                options.DefaultSignInScheme = CookieScheme;
                options.DefaultSignOutScheme = CookieScheme;
            })
            .AddPolicyScheme(DeviceOrCookieScheme, DeviceOrCookieScheme, options =>
            {
                options.ForwardDefaultSelector = context => DeviceBearer.Carries(context.Request) ? DeviceScheme : CookieScheme;
                options.ForwardChallenge = CookieScheme;
                options.ForwardForbid = CookieScheme;
                options.ForwardSignIn = CookieScheme;
                options.ForwardSignOut = CookieScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, DeviceBearerHandler>(DeviceScheme, _ => { })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                // Secure whenever the request is https, which behind Cloudflare is what X-Forwarded-Proto says.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.SlidingExpiration = true;
                options.LoginPath = "/login";
                options.ReturnUrlParameter = "returnUrl";
                options.EventsType = typeof(CookieSessionEvents);
            });
        services.AddScoped<CookieSessionEvents>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
        services.AddSingleton<IAuthorizationHandler, AdminAuthorizationHandler>();

        // Behind Cloudflare the app sees http from the tunnel; the forwarded proto and host make the cookie Secure and
        // redirects https. X-Forwarded-For is left alone (McpLoopback refuses any request that came through a proxy).
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // The cookie's keys live in Postgres, so a redeploy does not sign everyone out.
        services.AddDataProtection().SetApplicationName("codex");
        services.AddOptions<KeyManagementOptions>()
            .Configure<NpgsqlDataSource>((options, dataSource) => options.XmlRepository = new PostgresXmlRepository(dataSource));
    }
}
