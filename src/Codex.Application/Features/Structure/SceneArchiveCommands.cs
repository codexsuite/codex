using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Archive a scene (off the board, out of the numbering and out of clearance) and bring it back.
[RegisterSingleton]
public sealed class SceneArchiveCommands(NpgsqlDataSource dataSource, IBookAccess access, IBookEvents events)
{
    private sealed record SceneRow(Guid BookId, Guid ChapterId, string Position, bool Archived, bool ParentArchived);

    private const string UsesSql =
        """
        SELECT o.use_id AS UseId, o.reference_id AS SourceId, r.title AS SourceTitle
        FROM reference_use_outline o JOIN reference r ON r.id = o.reference_id
        WHERE o.scene_id = @sceneId ORDER BY o.book_order
        """;

    public async Task<ArchiveSceneResult> ArchiveAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, notFound: "Scene not found.");
        Guid bookId;
        List<SceneUseDto> uses;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var scene = await LockSceneAsync(connection, transaction, sceneId, cancellationToken);
            if (scene.Archived)
            {
                throw ApiException.Conflict("The scene is already archived.");
            }
            bookId = scene.BookId;
            // The uses the clearance loses are exactly those the reference outline shows for the scene now.
            uses = (await connection.QueryAsync<SceneUseDto>(new CommandDefinition(
                UsesSql, new { sceneId }, transaction, cancellationToken: cancellationToken))).ToList();
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scene SET archived_at = now(), updated_at = now() WHERE id = @sceneId",
                new { sceneId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(bookId, "scene"));
        return new ArchiveSceneResult(sceneId, uses);
    }

    public async Task<RestoreSceneResult> RestoreAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, notFound: "Scene not found.");
        Guid bookId;
        List<SceneUseDto> uses;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var scene = await LockSceneAsync(connection, transaction, sceneId, cancellationToken);
            if (!scene.Archived)
            {
                throw ApiException.Conflict("The scene is not archived.");
            }
            if (scene.ParentArchived)
            {
                throw ApiException.Conflict("Its chapter is archived: restore the chapter first.");
            }
            bookId = scene.BookId;
            var position = await FreePositionAsync(connection, transaction, scene, cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scene SET archived_at = NULL, position = @position, updated_at = now() WHERE id = @sceneId",
                new { sceneId, position }, transaction, cancellationToken: cancellationToken));
            uses = (await connection.QueryAsync<SceneUseDto>(new CommandDefinition(
                UsesSql, new { sceneId }, transaction, cancellationToken: cancellationToken))).ToList();
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(bookId, "scene"));
        return new RestoreSceneResult(sceneId, uses);
    }

    public async Task<IReadOnlyList<ArchivedSceneDto>> ListAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Chapter, chapterId, BookRole.View, cancellationToken, notFound: "Chapter not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM chapter WHERE id = @chapterId)", new { chapterId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound("Chapter not found.");
        }
        var rows = await connection.QueryAsync<ArchivedSceneDto>(new CommandDefinition(
            """
            SELECT s.id, co.chapter_number::int AS ChapterNumber,
                   ((SELECT count(*) FROM scene l WHERE l.chapter_id = s.chapter_id AND l.archived_at IS NULL AND l.position < s.position) + 1)::int AS WasSceneNumber,
                   s.title, s.summary, coalesce(p.word_count, 0)::int AS Words,
                   (SELECT count(*) FROM reference_use u WHERE u.scene_id = s.id AND u.archived_at IS NULL)::int AS UseCount
            FROM scene s
            LEFT JOIN chapter_outline co ON co.id = s.chapter_id
            LEFT JOIN prose p ON p.scene_id = s.id
            WHERE s.chapter_id = @chapterId AND s.archived_at IS NOT NULL
            ORDER BY s.archived_at DESC, s.id
            """,
            new { chapterId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private static async Task<SceneRow> LockSceneAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sceneId, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<SceneRow>(new CommandDefinition(
            """
            SELECT s.book_id AS BookId, s.chapter_id AS ChapterId, s.position, s.archived_at IS NOT NULL AS Archived,
                   (c.archived_at IS NOT NULL OR a.archived_at IS NOT NULL) AS ParentArchived
            FROM scene s JOIN chapter c ON c.id = s.chapter_id JOIN act a ON a.id = c.act_id
            WHERE s.id = @sceneId FOR UPDATE OF s
            """,
            new { sceneId }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Scene not found.");

    // The scene keeps its old key unless a live scene took it meanwhile; then it goes right after that one.
    private static async Task<string> FreePositionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, SceneRow scene, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@chapterId::text, 0))",
            new { scene.ChapterId }, transaction, cancellationToken: cancellationToken));
        var live = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT position FROM scene WHERE chapter_id = @chapterId AND archived_at IS NULL AND position >= @position ORDER BY position LIMIT 2",
            new { scene.ChapterId, scene.Position }, transaction, cancellationToken: cancellationToken))).ToList();
        if (live.Count == 0 || live[0] != scene.Position)
        {
            return scene.Position;
        }
        return FractionalKey.Between(scene.Position, live.Count > 1 ? live[1] : null);
    }
}
