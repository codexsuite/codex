using Agoda.IoC.Core;
using Codex.Contracts;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.References;

// Bringing back a source ISourceCommands.ArchiveAsync removed (the MCP undo of archive_source, #20). Archiving unwraps the
// uses into plain text, archives them and the source, and clears the best guess of the open quote flags that named it.
// Restore clears the source's archived_at and points those flags back at it; the uses come back on their own when the
// prose that held their links is restored (prose_sync_embeds un-archives a use whose link is in the body again).
public interface ISourceRestoreCommands
{
    // The open quote flags whose best guess is the source: the ones ArchiveAsync would clear.
    Task<IReadOnlyList<Guid>> FlagsAsync(Guid sourceId, CancellationToken cancellationToken);

    // Null when the source can be restored: the problem otherwise (it is live again, or was merged since).
    Task<string?> RestoreProblemAsync(Guid sourceId, CancellationToken cancellationToken);

    Task RestoreAsync(Guid sourceId, IReadOnlyList<Guid> flags, CancellationToken cancellationToken, EventProvenance? provenance = null);
}

[RegisterSingleton]
public sealed class SourceRestoreCommands(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookAccess access) : ISourceRestoreCommands
{
    public async Task<IReadOnlyList<Guid>> FlagsAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Source, sourceId, BookRole.View, cancellationToken, $"Source {sourceId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT id FROM quote_flag WHERE reference_id = @sourceId ORDER BY id", new { sourceId }, cancellationToken: cancellationToken))).ToList();
    }

    public async Task<string?> RestoreProblemAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<(bool Archived, Guid? MergedInto)?>(new CommandDefinition(
            "SELECT archived_at IS NOT NULL, merged_into_id FROM reference WHERE id = @sourceId", new { sourceId }, cancellationToken: cancellationToken));
        return row switch
        {
            null => "The source this write removed no longer exists.",
            { Archived: false } => "The source this write removed is back already.",
            { MergedInto: not null } => "The source this write removed has since been merged into another.",
            _ => null,
        };
    }

    public async Task RestoreAsync(Guid sourceId, IReadOnlyList<Guid> flags, CancellationToken cancellationToken, EventProvenance? provenance = null)
    {
        await access.RequireAsync(BookItem.Source, sourceId, BookRole.Write, cancellationToken, $"Source {sourceId} was not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference SET archived_at = NULL WHERE id = @sourceId AND merged_into_id IS NULL", new { sourceId }, transaction, cancellationToken: cancellationToken));
        // Only flags still open with no best guess: one the author has resolved or pointed elsewhere since is left.
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE quote_flag SET reference_id = @sourceId WHERE id = ANY(@ids) AND reference_id IS NULL", new { sourceId, ids = flags.ToArray() },
            transaction, cancellationToken: cancellationToken));
        await SourceEvents.RecordAsync(connection, transaction, sourceId, currentUser.Id, SourceEvents.Restored, new { }, cancellationToken, provenance);
        await transaction.CommitAsync(cancellationToken);
    }
}
