using Codex.Application.Identity;

namespace Codex.Application.Mcp;

// Who an MCP call acts as and what it may do (#32): a personal access token's user and scopes, with the token's id and
// name for provenance, or the local MCP user with every scope (TokenId null).
public sealed record McpGrant(Guid UserId, Guid? TokenId, string? TokenName, IReadOnlyList<string> Scopes)
{
    public static McpGrant Local(Guid userId) => new(userId, null, null, ApiTokenScopes.All);

    public static McpGrant From(ApiTokenGrant token) => new(token.UserId, token.TokenId, token.Name, token.Scopes);
}
