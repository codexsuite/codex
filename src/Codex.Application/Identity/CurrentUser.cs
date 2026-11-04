using System.Security.Claims;
using Agoda.IoC.Core;
using Codex.Application.Mcp;
using Codex.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Identity;

// Who the current call acts as, in this order:
//   1. a user put in place for a block of code by ActAs (work that outlives its request, such as an MCP background draft);
//   2. the request's signed-in user: the NameIdentifier claim of the cookie (or, on /mcp, the principal the guard put on
//      the request: the bearer token's user, or the local MCP user);
//   3. in the stdio MCP process (McpStdioTransport is registered there alone), the configured MCP user (McpUser).
// Anything else is a bug (the fallback policy keeps anonymous requests out), so it throws rather than guess a user.
[RegisterSingleton]
public sealed class CurrentUser(IServiceProvider services) : ICurrentUser
{
    private static readonly AsyncLocal<Guid?> Acting = new();

    public Guid Id => Acting.Value ?? FromRequest() ?? FromStdio()
        ?? throw new InvalidOperationException("No signed-in user: this call has no request user and is not the MCP process.");

    // Acts as userId until the result is disposed (on this async flow only).
    public static IDisposable ActAs(Guid userId)
    {
        var previous = Acting.Value;
        Acting.Value = userId;
        return new Restore(previous);
    }

    private Guid? FromRequest()
    {
        var user = services.GetService<IHttpContextAccessor>()?.HttpContext?.User;
        return user?.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
    }

    private Guid? FromStdio() => services.GetService<McpStdioTransport>() is null ? null : services.GetRequiredService<McpUser>().Id;

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Acting.Value = previous;
    }
}
