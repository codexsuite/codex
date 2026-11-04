using Agoda.IoC.Core;
using Codex.Application.Features.Ai.Providers;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Ai.Integrations;

// IAiIntegrationResolver over the ai_integration table: only the current user's own rows are ever visible, and the key
// is decrypted here, once, into the AiConnection that goes to the provider (never into a log, row or error).
[RegisterSingleton]
public sealed class AiIntegrationResolver(NpgsqlDataSource dataSource, ICurrentUser currentUser, IAiCredentialProtector protector) : IAiIntegrationResolver
{
    private sealed record Row(Guid Id, string Kind, string Label, string? BaseUrl, string Credential, string? DefaultModel);

    public async Task<ResolvedAiIntegration?> ResolveAsync(Guid? integrationId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """
            SELECT id AS Id, kind AS Kind, label AS Label, base_url AS BaseUrl, credential AS Credential, default_model AS DefaultModel
            FROM ai_integration
            WHERE owner_id = @owner AND archived_at IS NULL AND ((@id::uuid IS NOT NULL AND id = @id) OR (@id::uuid IS NULL AND is_default))
            """,
            new { owner = currentUser.Id, id = integrationId }, cancellationToken: cancellationToken));
        if (row == null)
        {
            return integrationId == null ? null : throw ApiException.NotFound("AI integration not found.");
        }

        var connectionToProvider = new AiConnection(row.Kind, row.BaseUrl is null ? null : new Uri(row.BaseUrl), protector.Unprotect(row.Credential));
        return new ResolvedAiIntegration(row.Id, row.Kind, row.Label, row.DefaultModel, connectionToProvider);
    }

    public async Task RecordUseAsync(Guid integrationId, string? error, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ai_integration SET last_used_at = now(), last_error = @error WHERE id = @integrationId AND owner_id = @owner",
            new { integrationId, error, owner = currentUser.Id }, cancellationToken: cancellationToken));
    }
}
