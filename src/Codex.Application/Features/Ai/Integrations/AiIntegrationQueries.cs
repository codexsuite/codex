using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Ai.Integrations;

// What Settings → AI and the Write draft panel read: the signed-in user's own integrations, default first, then in
// the order they were connected. Another user's rows are never selected, and no query here selects the credential.
[RegisterSingleton]
public sealed class AiIntegrationQueries(NpgsqlDataSource dataSource, ICurrentUser currentUser, IAiEndpointGuard guard, TimeProvider clock)
{
    private const string OrderBy = "ORDER BY is_default DESC, created_at, id";

    public async Task<AiSettingsDto> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        return new AiSettingsDto(rows.Select(r => r.ToDto(clock)).ToList(), guard.LocalEndpointsAllowed);
    }

    public async Task<AiModelChoicesDto> ModelsAsync(CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        return new AiModelChoicesDto(rows.Select(r => r.ToChoice()).ToList());
    }

    public async Task<AiIntegrationDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cancellationToken);
        return (rows.FirstOrDefault(r => r.Id == id) ?? throw ApiException.NotFound("Integration not found.")).ToDto(clock);
    }

    private async Task<IReadOnlyList<AiIntegrationRow>> RowsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<AiIntegrationRow>(new CommandDefinition(
            $"SELECT {AiIntegrationRow.Columns} FROM ai_integration WHERE owner_id = @owner AND archived_at IS NULL {OrderBy}",
            new { owner = currentUser.Id }, cancellationToken: cancellationToken))).ToList();
    }
}
