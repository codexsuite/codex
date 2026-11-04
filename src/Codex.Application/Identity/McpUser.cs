using Agoda.IoC.Core;
using Codex.Application.Mcp;
using Codex.Contracts;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Codex.Application.Identity;

// The one user the local MCP transports act as (stdio, and /mcp from this machine without a token in Development):
// CODEX_MCP_TOKEN's user with that token's scopes when it is set (#32), else CODEX_MCP_USER_EMAIL (JD's seeded email by
// default) with every scope. Looked up once, on first use; McpAccess checks the token again on every call.
[RegisterSingleton]
public sealed class McpUser(NpgsqlDataSource dataSource, IConfiguration configuration)
{
    public const string EmailKey = "CODEX_MCP_USER_EMAIL";
    public const string TokenKey = "CODEX_MCP_TOKEN";
    public const string DefaultEmail = "jd@codex.local";

    private readonly Lazy<McpGrant> _grant = new(() => Resolve(dataSource, configuration));

    public Guid Id => Grant.UserId;

    public McpGrant Grant => _grant.Value;

    public string? Token => configuration[TokenKey] is { Length: > 0 } token ? token : null;

    private static McpGrant Resolve(NpgsqlDataSource dataSource, IConfiguration configuration)
    {
        using var connection = dataSource.OpenConnection();
        if (configuration[TokenKey] is { Length: > 0 } token)
        {
            return ApiTokens.Find(connection, token) is { } grant
                ? McpGrant.From(grant)
                : throw new InvalidOperationException($"{TokenKey}: the token is unknown, revoked or expired, or its user is disabled.");
        }
        var email = configuration[EmailKey] is { Length: > 0 } configured ? configured : DefaultEmail;
        return McpGrant.Local(connection.QuerySingleOrDefault<Guid?>(
                   "SELECT id FROM app_user WHERE lower(email) = lower(@email) AND disabled_at IS NULL", new { email })
               ?? throw new InvalidOperationException($"{EmailKey}: no active user has the email {email}."));
    }
}
