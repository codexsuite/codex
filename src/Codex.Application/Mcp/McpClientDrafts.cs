using Agoda.IoC.Core;
using Codex.Contracts;
using Dapper;
using Npgsql;

namespace Codex.Application.Mcp;

// The two writes MCP makes to ai_generation (Features/Ai's table) that Features/Ai has no command for: storing a draft the
// agent wrote itself (submit_draft: model 'client', created_via 'mcp') and stamping a draft Codex generated for an MCP
// client (created_via 'mcp', client_name, and the personal access token's id and name). The SQL stays here, in one
// place, with the conductor's sanction.
// TODO: a client-draft command in Features/Ai (and a created_via on DraftGenerator's request) replaces this class; the
// Features/Ai owner (L11) adds it, then this file is deleted.
[RegisterSingleton]
public sealed class McpClientDrafts(NpgsqlDataSource dataSource, ICurrentUser currentUser)
{
    // A pending draft for a beat (or the end of a scene) with the agent's text; returns the generation id.
    public async Task<Guid> InsertAsync(
        Guid bookId, Guid sceneId, Guid? beatId, string text, int words, McpCaller caller, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO ai_generation (book_id, scene_id, beat_id, kind, model, request, output, status, created_by, created_via, client_name,
                                       api_token_id, api_token_name)
            VALUES (@bookId, @sceneId, @beatId, @kind, 'client', @request::jsonb, @text, 'pending', @userId, 'mcp', @clientName,
                    @tokenId, @tokenName)
            RETURNING id
            """,
            new
            {
                bookId, sceneId, beatId, kind = beatId == null ? "continue" : "beat_prose",
                request = System.Text.Json.JsonSerializer.Serialize(new { attempt = 1, targetWords = words, context = Array.Empty<string>(), writtenBy = caller.ClientName }),
                text, userId = currentUser.Id, clientName = caller.ClientName, tokenId = caller.TokenId, tokenName = caller.TokenName,
            }, cancellationToken: cancellationToken));
    }

    // Marks a draft Codex generated as asked for over MCP, with the client's and token's names.
    public async Task MarkAsync(Guid generationId, McpCaller caller, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ai_generation SET created_via = 'mcp', client_name = @clientName, api_token_id = @tokenId, api_token_name = @tokenName
            WHERE id = @generationId AND created_by = @userId
            """,
            new { generationId, clientName = caller.ClientName, tokenId = caller.TokenId, tokenName = caller.TokenName, userId = currentUser.Id },
            cancellationToken: cancellationToken));
    }
}
