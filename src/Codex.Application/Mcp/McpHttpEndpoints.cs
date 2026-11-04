using System.Security.Claims;
using Codex.Application.Identity;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codex.Application.Mcp;

// Streamable HTTP at /mcp (#20 Part 7). Every /mcp request is checked here before the MCP handler sees it (the check
// wraps each endpoint's final request delegate, so it holds whatever handlers MapMcp maps):
//   - Authorization: Bearer cdx_pat_… (#32) acts as the token's user with its scopes, from anywhere. An unknown,
//     revoked or expired token, or a disabled user's, gets 401.
//   - With no token, a request from this machine (McpLoopback) acts as the local MCP user (McpUser), in Development
//     only. Anything else gets 401.
// The grant goes on the request as its principal, so CurrentUser, and with it the ownership rule, treat it like any
// signed-in request; McpScopes checks its scopes per tool. The /api cookie routes never read these tokens.
public sealed class McpHttpEndpoints : IEndpointModule
{
    public const string Path = "/mcp";

    private const string BearerPrefix = "Bearer ";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp(Path).AllowAnonymous().Finally(Guard);
        // Stateless transport has no stream to GET and no session to DELETE, so the SDK maps neither; without these
        // the SPA fallback would answer them with index.html.
        endpoints.MapMethods(Path, [HttpMethods.Get, HttpMethods.Delete], () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed))
            .AllowAnonymous()
            .Finally(Guard);
    }

    private static void Guard(EndpointBuilder endpoint)
    {
        var next = endpoint.RequestDelegate;
        if (next == null)
        {
            return;
        }
        endpoint.RequestDelegate = async context =>
        {
            if (await GrantAsync(context.Request, context.Connection, context.RequestServices, context.RequestAborted) is not { } grant)
            {
                await RefuseAsync(context.Response, context.RequestAborted);
                return;
            }
            McpAccess.Put(context.Items, grant);
            var scheme = grant.TokenId == null ? "mcp" : "mcp-token";
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, grant.UserId.ToString())], scheme));
            await next(context);
        };
    }

    private static async Task<McpGrant?> GrantAsync(
        HttpRequest request, ConnectionInfo connection, IServiceProvider services, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.Length > 0)
        {
            return authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
                   && await services.GetRequiredService<ApiTokens>().AuthenticateAsync(authorization[BearerPrefix.Length..].Trim(), cancellationToken) is { } token
                ? McpGrant.From(token)
                : null;
        }
        return services.GetRequiredService<IHostEnvironment>().IsDevelopment() && McpLoopback.Allows(connection, request)
            ? await services.GetRequiredService<McpAccess>().LocalAsync(cancellationToken)
            : null;
    }

    // 401 with a Bearer challenge. TODO: phase 2 of #32 (OAuth) adds resource_metadata to the challenge.
    private static Task RefuseAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = "Bearer realm=\"codex\"";
        return response.WriteAsJsonAsync(
            ApiException.Unauthorized("Send a Codex personal access token: Authorization: Bearer cdx_pat_…. Create one under Account → Agent access.")
                .ToApiError(), cancellationToken);
    }
}
