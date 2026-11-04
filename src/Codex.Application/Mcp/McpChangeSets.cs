using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Dapper;
using Npgsql;

namespace Codex.Application.Mcp;

// The change_set rows behind undo (M14) and the agent's prose revisions (M12). Every MCP write records one change set:
// the book, the tool, a one-line summary, who wrote it (McpCaller: client, session and token) and the steps that reverse
// it. Its id, as "u_<id>", is the result's undo_id.
[RegisterSingleton]
public sealed class McpChangeSets(NpgsqlDataSource dataSource, ICurrentUser currentUser)
{
    public const string UndoPrefix = "u_";

    // How long an undo id stays good (M14).
    public static readonly TimeSpan UndoWindow = TimeSpan.FromHours(24);

    public sealed record ChangeSet(Guid Id, Guid BookId, string Tool, string Summary, IReadOnlyList<McpUndoStep> Steps, bool Undone, bool Expired);

    private sealed record Row(Guid Id, Guid BookId, string Tool, string Summary, string Undo, bool Undone, bool Expired);

    public async Task<string> RecordAsync(
        Guid bookId, string tool, string summary, McpCaller caller, IReadOnlyList<McpUndoStep> steps, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO change_set (book_id, source, client_name, mcp_session, tool, summary, undo, created_by, api_token_id, api_token_name)
            VALUES (@bookId, 'agent', @clientName, @session, @tool, @summary, @undo::jsonb, @userId, @tokenId, @tokenName)
            RETURNING id
            """,
            new
            {
                bookId, clientName = caller.ClientName, session = caller.Session, tool, summary,
                undo = JsonSerializer.Serialize(steps, McpJson.Options), userId = currentUser.Id,
                tokenId = caller.TokenId, tokenName = caller.TokenName,
            },
            cancellationToken: cancellationToken));
        return UndoPrefix + id.ToString("N", CultureInfo.InvariantCulture);
    }

    // The change set behind an undo id, when the current user can open its book (#80); null otherwise. Undo then checks
    // the role it needs there (UndoTools): anyone who may write in the book may reverse a write in it, as in the UI.
    public async Task<ChangeSet?> FindAsync(string undoId, CancellationToken cancellationToken)
    {
        var text = undoId.Trim();
        if (!text.StartsWith(UndoPrefix, StringComparison.Ordinal) || !Guid.TryParse(text[UndoPrefix.Length..], out var id))
        {
            return null;
        }
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """
            SELECT c.id, c.book_id AS BookId, c.tool, c.summary, c.undo::text AS Undo, c.undone_at IS NOT NULL AS Undone,
                   c.created_at < now() - @window AS Expired
            FROM change_set c JOIN book_access a ON a.book_id = c.book_id AND a.user_id = @user
            WHERE c.id = @id
            """,
            new { id, user = currentUser.Id, window = UndoWindow }, cancellationToken: cancellationToken));
        return row == null
            ? null
            : new ChangeSet(row.Id, row.BookId, row.Tool, row.Summary,
                JsonSerializer.Deserialize<List<McpUndoStep>>(row.Undo, McpJson.Options) ?? [], row.Undone, row.Expired);
    }

    // Claims a change set for undo: stamps undone_at first, so two undos of the same id can't both run. False when
    // another undo already holds it.
    public async Task<bool> ClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE change_set SET undone_at = now() WHERE id = @id AND undone_at IS NULL",
            new { id }, cancellationToken: cancellationToken)) == 1;
    }

    // Gives a claim back when the undo did not happen, so the id can be tried again.
    public async Task ReleaseAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE change_set SET undone_at = NULL WHERE id = @id", new { id }, cancellationToken: cancellationToken));
    }

    // The same revision for a write made by another feature's command (which owns its own prose transaction): cut
    // straight after it, holding the body the command started from.
    public async Task CutAgentRevisionAsync(Guid sceneId, string before, McpCaller caller, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AgentRevision(sceneId, () => before, caller)(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Runs inside an IProseStore write (ProseWriteOptions.InTransaction): cuts the 'agent' revision holding the body as
    // it was before the agent's edit, once per scene per MCP session (M12). With no session (stateless HTTP) every
    // call is its own session, so every call cuts one.
    public Func<DbConnection, DbTransaction, CancellationToken, Task> AgentRevision(Guid sceneId, Func<string?> before, McpCaller caller) =>
        (connection, transaction, cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO prose_revision (scene_id, body, source, created_by, client_name, mcp_session, api_token_id, api_token_name)
            SELECT @sceneId, @body, 'agent', @userId, @clientName, @session, @tokenId, @tokenName
            WHERE @body IS NOT NULL
              AND (@session::text IS NULL OR NOT EXISTS (
                  SELECT 1 FROM prose_revision WHERE scene_id = @sceneId AND mcp_session = @session::text))
            """,
            new
            {
                sceneId, body = before(), userId = currentUser.Id, clientName = caller.ClientName, session = caller.Session,
                tokenId = caller.TokenId, tokenName = caller.TokenName,
            },
            transaction, cancellationToken: cancellationToken));
}
