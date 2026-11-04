using Agoda.IoC.Core;
using Codex.Contracts;
using Dapper;
using Npgsql;

namespace Codex.Application.Mcp;

// Which of the caller's AI integrations draft_prose writes with (#36): the one named by id or label (case-insensitive),
// else their default. It reads only the caller's own rows and never the key: ai_integration.owner_id is the drafting
// user, so a draft in a book shared with them uses their integration, never the book owner's (#80).
[RegisterSingleton]
public sealed class McpDraftIntegrations(NpgsqlDataSource dataSource, ICurrentUser currentUser)
{
    public sealed record Choice(Guid Id, string Kind, string? DefaultModel);

    // Null when `integration` is null and the caller has no default; a named one that is not theirs throws Invalid.
    public async Task<Choice?> ChooseAsync(string? integration, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(integration))
        {
            return await connection.QuerySingleOrDefaultAsync<Choice>(new CommandDefinition(
                "SELECT id AS Id, kind AS Kind, default_model AS DefaultModel FROM ai_integration WHERE owner_id = @owner AND is_default AND archived_at IS NULL",
                new { owner = currentUser.Id }, cancellationToken: cancellationToken));
        }

        var wanted = integration.Trim();
        var matches = (await connection.QueryAsync<Choice>(new CommandDefinition(
            """
            SELECT id AS Id, kind AS Kind, default_model AS DefaultModel FROM ai_integration
            WHERE owner_id = @owner AND archived_at IS NULL AND (id::text = lower(@wanted) OR lower(label) = lower(@wanted))
            ORDER BY created_at, id
            """,
            new { owner = currentUser.Id, wanted }, cancellationToken: cancellationToken))).ToArray();
        return matches.Length > 0
            ? matches[0]
            : throw McpToolException.Invalid("integration", $"No AI integration \"{wanted}\" among yours. Connect one in Settings → AI.");
    }
}
