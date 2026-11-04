using Codex.Contracts;

namespace Codex.WebApi.Features.Auth;

// /login serves the SPA to anyone, so the sign-in page loads before there is a session. A static-file fallback, not
// an API route, so it stays an endpoint module (a controller action would change how index.html is served).
public sealed class LoginPageEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapFallbackToFile("/login", "index.html").AllowAnonymous();
}
