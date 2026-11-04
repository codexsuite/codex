using System.ComponentModel;
using Codex.Contracts;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// Safety: undo (#20, M14). Reverses one MCP write by its undo_id within 24 hours, through the same commands as the
// UI's Undo (McpUndo). Prose that was edited again since fails with conflict and nothing is undone. Undo needs write on
// the book (#80): a book the caller can no longer open is not_found, a role lowered to view is forbidden.
[McpServerToolType]
public sealed class UndoTools(McpChangeSets changeSets, McpUndo undo, McpRefs refs, McpAccess access, IBookAccess bookAccess)
{
    public sealed record UndoResult(string Undid) : McpWriteResult;

    [McpServerTool(Name = "undo", Title = "Take that back", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Reverses one write by the undo_id its result returned, within 24 hours: prose goes back to the body before the edit, " +
                 "moves go back, archived scenes come back. Fails with conflict, writing nothing, when the prose was edited again since.")]
    public async Task<UndoResult> UndoAsync(
        McpServer server,
        [Description("The undo_id from a write result.")] string undo_id,
        CancellationToken cancellationToken = default)
    {
        var changeSet = await changeSets.FindAsync(undo_id, cancellationToken)
                        ?? throw McpToolException.NotFound($"No write with undo id \"{undo_id}\".");
        // Undoing a write needs the scope that write needed (#32): a cite_source is undone with codex:references.
        McpScopes.Require(access.Current, changeSet.Tool);
        await bookAccess.RequireAsync(changeSet.BookId, BookRole.Write, cancellationToken);
        if (changeSet.Undone)
        {
            throw new McpToolException(McpErrorCodes.Blocked, "That write has already been undone.") { Rule = "already_undone" };
        }
        if (changeSet.Expired)
        {
            throw new McpToolException(McpErrorCodes.Blocked, "Undo ids last 24 hours; this one has expired. Restore from History in Write instead.")
            {
                Rule = "undo_expired",
            };
        }
        if (!await changeSets.ClaimAsync(changeSet.Id, cancellationToken))
        {
            throw new McpToolException(McpErrorCodes.Blocked, "That write has already been undone.") { Rule = "already_undone" };
        }
        try
        {
            // Every precondition is read before the first step runs: a conflict leaves everything as it is.
            await undo.CheckAsync(changeSet.Steps, cancellationToken);
            await undo.RunAsync(changeSet.Steps, cancellationToken);
        }
        catch (Exception)
        {
            await changeSets.ReleaseAsync(changeSet.Id, CancellationToken.None);
            throw;
        }
        if (await refs.AccessibleAsync(changeSet.BookId, cancellationToken) is { IsSeries: true } series)
        {
            // A series has no outline to refresh.
            return new UndoResult(changeSet.Summary) { Item = series.Item, SideEffects = [$"Undone: {changeSet.Summary}."] };
        }
        var book = await refs.BookAsync(changeSet.BookId.ToString(), cancellationToken);
        await refs.OutlineAsync(book, McpRefs.SessionKey(server), cancellationToken);
        return new UndoResult(changeSet.Summary) { Item = book.Item, SideEffects = [$"Undone: {changeSet.Summary}."] };
    }
}
