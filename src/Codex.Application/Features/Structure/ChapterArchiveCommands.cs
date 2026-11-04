using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Archive a chapter ("Delete chapter": off the board, out of the numbering, its scenes out of clearance) and bring it
// back. The chapter's scenes keep their own archived_at, so Restore brings back exactly what was live.
[RegisterSingleton]
public sealed class ChapterArchiveCommands(NpgsqlDataSource dataSource, IBookAccess access, OutlineQuery outline, IBookEvents events)
{
    private sealed record ChapterRow(Guid BookId, Guid ActId, string Position, bool Archived, bool ActArchived);

    public async Task<ArchiveChapterResult> ArchiveAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Chapter, chapterId, BookRole.Write, cancellationToken, notFound: "Chapter not found.");
        Guid bookId;
        int sceneCount;
        List<SceneUseDto> uses;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var chapter = await LockChapterAsync(connection, transaction, chapterId, cancellationToken);
            if (chapter.Archived)
            {
                throw ApiException.Conflict("The chapter is already archived.");
            }
            bookId = chapter.BookId;
            sceneCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*)::int FROM scene WHERE chapter_id = @chapterId AND archived_at IS NULL",
                new { chapterId }, transaction, cancellationToken: cancellationToken));
            // The uses the clearance loses are exactly those the reference outline shows for the chapter's scenes now.
            uses = (await connection.QueryAsync<SceneUseDto>(new CommandDefinition(
                """
                SELECT o.use_id AS UseId, o.reference_id AS SourceId, r.title AS SourceTitle
                FROM reference_use_outline o
                JOIN reference r ON r.id = o.reference_id
                JOIN scene s ON s.id = o.scene_id
                WHERE s.chapter_id = @chapterId ORDER BY o.book_order
                """,
                new { chapterId }, transaction, cancellationToken: cancellationToken))).ToList();
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE chapter SET archived_at = now(), updated_at = now() WHERE id = @chapterId",
                new { chapterId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "chapter"));
        return new ArchiveChapterResult(chapterId, sceneCount, uses);
    }

    public async Task<OutlineChapterDto> RestoreAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Chapter, chapterId, BookRole.Write, cancellationToken, notFound: "Chapter not found.");
        Guid bookId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var chapter = await LockChapterAsync(connection, transaction, chapterId, cancellationToken);
            if (!chapter.Archived)
            {
                throw ApiException.Conflict("The chapter is not archived.");
            }
            if (chapter.ActArchived)
            {
                throw ApiException.Conflict("Its act is deleted: restore the act first.");
            }
            bookId = chapter.BookId;
            await SiblingKeys.LockAsync(connection, transaction, chapter.ActId, cancellationToken);
            // The chapter keeps its old key unless a live chapter took it meanwhile; then it goes right after that one.
            var live = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT position FROM chapter WHERE act_id = @actId AND archived_at IS NULL AND position >= @position ORDER BY position LIMIT 2",
                new { chapter.ActId, chapter.Position }, transaction, cancellationToken: cancellationToken))).ToList();
            var position = chapter.Position;
            if (live.Count > 0 && live[0] == chapter.Position)
            {
                position = FractionalKey.Between(chapter.Position, live.Count > 1 ? live[1] : null);
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE chapter SET archived_at = NULL, position = @position, updated_at = now() WHERE id = @chapterId",
                new { chapterId, position }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "chapter"));
        return (await outline.GetAsync(bookId, cancellationToken)).Acts
            .SelectMany(a => a.Chapters).Single(c => c.Id == chapterId);
    }

    private static async Task<ChapterRow> LockChapterAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid chapterId, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<ChapterRow>(new CommandDefinition(
            """
            SELECT c.book_id AS BookId, c.act_id AS ActId, c.position, c.archived_at IS NOT NULL AS Archived,
                   a.archived_at IS NOT NULL AS ActArchived
            FROM chapter c JOIN act a ON a.id = c.act_id JOIN book b ON b.id = c.book_id
            WHERE c.id = @chapterId AND b.archived_at IS NULL FOR UPDATE OF c
            """,
            new { chapterId }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Chapter not found.");
}
