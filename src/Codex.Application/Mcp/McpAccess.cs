using Agoda.IoC.Core;
using Codex.Application.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Mcp;

// The grant the current MCP call runs under (#32). On /mcp, McpHttpEndpoints' guard puts it on the request: the bearer
// token's, or the local user's. In the stdio process it is the local user's (McpUser). A local token is checked again on
// every call (CheckAsync), so revoking it, or disabling its user, stops a running stdio session at its next call.
[RegisterSingleton]
public sealed class McpAccess(IServiceProvider services, McpUser localUser, ApiTokens tokens)
{
    private const string ItemKey = "codex.mcp.grant";

    // This call's grant as it was checked: for provenance (McpCaller), not for deciding whether the call may run.
    public McpGrant Current => FromRequest() ?? localUser.Grant;

    // Puts a checked grant on an /mcp request (its HttpContext.Items).
    public static void Put(IDictionary<object, object?> requestItems, McpGrant grant) => requestItems[ItemKey] = grant;

    // The grant a call may run under, or unauthorized when the local token no longer works.
    public async Task<McpGrant> CheckAsync(CancellationToken cancellationToken)
        => FromRequest() ?? await LocalAsync(cancellationToken)
           ?? throw new McpToolException(McpErrorCodes.Unauthorized,
               $"{McpUser.TokenKey} no longer works: it was revoked or has expired, or its user was disabled.");

    // The local user's grant, with its token (if any) checked now; null when that token no longer works.
    public async Task<McpGrant?> LocalAsync(CancellationToken cancellationToken)
    {
        var local = localUser.Grant;
        if (local.TokenId == null)
        {
            return local;
        }
        return await tokens.AuthenticateAsync(localUser.Token, cancellationToken) is { } token ? McpGrant.From(token) : null;
    }

    private McpGrant? FromRequest() => services.GetService<IHttpContextAccessor>()?.HttpContext?.Items[ItemKey] as McpGrant;
}
