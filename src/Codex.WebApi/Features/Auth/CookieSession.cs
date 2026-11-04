using Agoda.IoC.Core;
using Codex.Application.Identity;
using Microsoft.AspNetCore.Authentication;

namespace Codex.WebApi.Features.Auth;

// Issues and clears this request's sign-in cookie.
[RegisterSingleton]
public sealed class CookieSession(IServiceProvider services)
{
    public Task SignInAsync(UserSnapshot user)
        => Http.SignInAsync(AuthServices.CookieScheme, AuthPrincipal.For(user), new AuthenticationProperties { IsPersistent = true });

    public Task SignOutAsync() => Http.SignOutAsync(AuthServices.CookieScheme);

    private HttpContext Http => services.GetRequiredService<IHttpContextAccessor>().HttpContext ?? throw new InvalidOperationException("No request to sign in on.");
}
