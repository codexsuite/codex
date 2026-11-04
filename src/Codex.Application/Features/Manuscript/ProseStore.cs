using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Manuscript;

// The access rule (#29, #80): a scene in a book the current user cannot open reads and writes as "Scene not found.";
// reading needs view, writing needs write (forbidden below it), for every caller (the API, MCP, the features that save
// prose through here).
public sealed class ProseStore(NpgsqlDataSource dataSource, ICurrentUser currentUser, IBookEvents events, IBookAccess access) : IProseStore
{
    // The origin of the editor's own saves (PUT /api/scenes/{id}/prose).
    public const string EditorOrigin = "editor";

    private static readonly HashSet<string> RevisionSources = ["import", "manual", "ai_apply", "restore"];

    private static readonly ProseWriteOptions NoOptions = new();

    private sealed record SceneRow(Guid BookId, DateTimeOffset UpdatedAt);

    private sealed record ProseRow(string Body, int Version, DateTimeOffset UpdatedAt);

    public async Task<ProseDto> GetAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var scene = await connection.QuerySingleOrDefaultAsync<SceneRow>(
            new CommandDefinition(
                "SELECT s.book_id AS BookId, s.updated_at AS UpdatedAt FROM scene s JOIN book_access a ON a.book_id = s.book_id WHERE s.id = @sceneId AND a.user_id = @user",
                new { sceneId, user = currentUser.Id }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Scene not found.");
        var prose = await connection.QuerySingleOrDefaultAsync<ProseRow>(
            new CommandDefinition("SELECT body, version, updated_at AS UpdatedAt FROM prose WHERE scene_id = @sceneId", new { sceneId }, cancellationToken: cancellationToken));

        return prose == null
            ? new ProseDto(sceneId, "", 0, scene.UpdatedAt)
            : new ProseDto(sceneId, prose.Body, prose.Version, prose.UpdatedAt);
    }

    public Task<int> UpdateAsync(
        Guid sceneId,
        Func<string, string> edit,
        int expectedVersion,
        string origin,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(sceneId, edit, expectedVersion, origin, NoOptions, cancellationToken);

    // One scene is a batch of one: the same lock, version check, write, steps and event.
    public async Task<int> UpdateAsync(
        Guid sceneId,
        Func<string, string> edit,
        int expectedVersion,
        string origin,
        ProseWriteOptions options,
        CancellationToken cancellationToken = default) =>
        (await WriteManyAsync([(sceneId, expectedVersion, edit)], origin, options, cancellationToken))[0];

    public Task<IReadOnlyList<int>> UpdateManyAsync(
        IReadOnlyList<(Guid sceneId, int expectedVersion, Func<string, string> edit)> edits,
        string origin,
        CancellationToken cancellationToken = default) =>
        WriteManyAsync(edits, origin, NoOptions, cancellationToken);

    public Task<IReadOnlyList<int>> UpdateManyAsync(
        IReadOnlyList<(Guid sceneId, int expectedVersion, Func<string, string> edit)> edits,
        string origin,
        ProseWriteOptions options,
        CancellationToken cancellationToken = default) =>
        WriteManyAsync(edits, origin, options, cancellationToken);

    private async Task<IReadOnlyList<int>> WriteManyAsync(
        IReadOnlyList<(Guid sceneId, int expectedVersion, Func<string, string> edit)> edits,
        string origin,
        ProseWriteOptions options,
        CancellationToken cancellationToken)
    {
        var books = new Dictionary<Guid, Guid>();
        var versions = new Dictionary<Guid, int>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                if (options.BeforeWrites != null)
                {
                    await options.BeforeWrites(connection, transaction, cancellationToken);
                }

                var currents = new Dictionary<Guid, ProseRow?>();
                var replaced = new HashSet<Guid>();
                foreach (var (sceneId, expectedVersion, _) in edits.OrderBy(e => e.sceneId))
                {
                    // A scene may be one BeforeWrites just inserted, so the write check reads inside the transaction.
                    var scene = await connection.QuerySingleOrDefaultAsync<SceneRow>(new CommandDefinition(
                        """
                        SELECT s.book_id AS BookId, s.updated_at AS UpdatedAt FROM scene s
                        WHERE s.id = @sceneId
                          AND EXISTS (SELECT 1 FROM book_access a WHERE a.book_id = s.book_id AND a.user_id = @user AND a.role_rank >= 3)
                        FOR UPDATE OF s
                        """,
                        new { sceneId, user = currentUser.Id }, transaction, cancellationToken: cancellationToken))
                        ?? await RefuseAsync(sceneId, cancellationToken);
                    books[sceneId] = scene.BookId;
                    var current = await connection.QuerySingleOrDefaultAsync<ProseRow>(new CommandDefinition(
                        "SELECT body, version, updated_at AS UpdatedAt FROM prose WHERE scene_id = @sceneId",
                        new { sceneId }, transaction, cancellationToken: cancellationToken));
                    currents[sceneId] = current;
                    var currentVersion = current?.Version ?? 0;
                    if (currentVersion > expectedVersion && options.LastWriteWins)
                    {
                        replaced.Add(sceneId);
                    }
                    else if (currentVersion != expectedVersion)
                    {
                        throw ApiException.Conflict($"The scene changed since version {expectedVersion}; it is now at version {currentVersion}.");
                    }
                }

                foreach (var (sceneId, _, edit) in edits)
                {
                    var current = currents[sceneId];
                    var body = edit(current?.Body ?? "") ?? throw ApiException.Validation("Prose body is required.");
                    versions[sceneId] = (current?.Version ?? 0) + 1;
                    if (replaced.Contains(sceneId) && current!.Body != body)
                    {
                        await KeepReplacedAsync(connection, transaction, sceneId, current.Body, cancellationToken);
                    }
                    await WriteAsync(connection, transaction, sceneId, body, versions[sceneId], origin, options.GenerationId, cancellationToken);
                }

                if (options.InTransaction != null)
                {
                    await options.InTransaction(connection, transaction, cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.RaiseException or PostgresErrorCodes.ForeignKeyViolation)
            {
                throw ApiException.Validation(e.MessageText);
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw ApiException.Conflict(e.MessageText);
            }
        }

        foreach (var (sceneId, _, _) in edits)
        {
            await events.PublishAsync(new ProseSaved(books[sceneId], sceneId, versions[sceneId], origin));
        }

        return edits.Select(e => versions[e.sceneId]).ToList();
    }

    // R6 (two tabs, last write wins): the text a stale save overwrites is kept as a 'replaced' revision. When the
    // newest revision already holds that text (the other session's autosave) it is relabelled instead of repeated.
    // No writable scene: not found without access, forbidden below write (IBookAccess words both).
    private async Task<SceneRow> RefuseAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, "Scene not found.");
        throw ApiException.NotFound("Scene not found.");
    }

    private async Task KeepReplacedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sceneId, string body, CancellationToken cancellationToken)
    {
        var relabelled = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE prose_revision SET source = 'replaced'
            WHERE id = (SELECT id FROM prose_revision WHERE scene_id = @sceneId ORDER BY created_at DESC, id DESC LIMIT 1)
              AND source = 'autosave' AND body = @body
            """,
            new { sceneId, body }, transaction, cancellationToken: cancellationToken));
        if (relabelled == 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO prose_revision (scene_id, body, source, created_by) VALUES (@sceneId, @body, 'replaced', @userId)",
                new { sceneId, body, userId = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        }
    }

    private async Task WriteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sceneId, string body, int version, string origin, Guid? generationId,
        CancellationToken cancellationToken)
    {
        var args = new { sceneId, body, version, userId = currentUser.Id };
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO prose (scene_id, body, version, updated_by) VALUES (@sceneId, @body, @version, @userId)
            ON CONFLICT (scene_id) DO UPDATE SET body = excluded.body, version = excluded.version, updated_by = excluded.updated_by
            """,
            args, transaction, cancellationToken: cancellationToken));

        if (RevisionSources.Contains(origin))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO prose_revision (scene_id, body, source, created_by, generation_id) VALUES (@sceneId, @body, @origin, @userId, @generationId)",
                new { sceneId, body, origin, userId = currentUser.Id, generationId }, transaction, cancellationToken: cancellationToken));
        }
        else if (origin == EditorOrigin)
        {
            // The editor's saves: a revision is cut for the first save after any other revision (an apply, a restore,
            // an agent edit, an import); further saves within ten minutes update that autosave revision in place.
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE prose_revision SET body = @body
                WHERE id = (SELECT id FROM prose_revision WHERE scene_id = @sceneId ORDER BY created_at DESC, id DESC LIMIT 1)
                  AND source = 'autosave' AND created_at > now() - interval '10 minutes'
                """,
                new { sceneId, body }, transaction, cancellationToken: cancellationToken));
            if (updated == 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO prose_revision (scene_id, body, source, created_by) VALUES (@sceneId, @body, 'autosave', @userId)",
                    new { sceneId, body, userId = currentUser.Id }, transaction, cancellationToken: cancellationToken));
            }
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO prose_revision (scene_id, body, source, created_by)
                SELECT @sceneId, @body, 'autosave', @userId
                WHERE NOT EXISTS (SELECT 1 FROM prose_revision WHERE scene_id = @sceneId AND created_at > now() - interval '10 minutes')
                """,
                new { sceneId, body, userId = currentUser.Id }, transaction, cancellationToken: cancellationToken));
        }
    }
}
