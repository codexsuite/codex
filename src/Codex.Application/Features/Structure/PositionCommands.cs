using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Reorders acts, chapters and scenes by writing a fractional key between the new neighbours. Every move answers with
// the book's outline, renumbered.
[RegisterSingleton]
public sealed class PositionCommands(NpgsqlDataSource dataSource, IBookAccess access, OutlineQuery outline, IBookEvents events)
{
    private sealed record Target(Guid BookId, bool Archived);

    public async Task<OutlineDto> MoveActAsync(Guid actId, MoveActRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Act, actId, BookRole.Write, cancellationToken, notFound: "Act not found.");
        Guid bookId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM act WHERE id = @actId AND archived_at IS NULL FOR UPDATE",
                new { actId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Act not found.");
            await SiblingKeys.LockAsync(connection, transaction, bookId, cancellationToken);
            var siblings = await SiblingKeys.SiblingsAsync(connection, transaction,
                "SELECT id, position FROM act WHERE book_id = @parentId AND archived_at IS NULL AND id <> @movingId ORDER BY position, id",
                bookId, actId, cancellationToken);
            var position = SiblingKeys.KeyAfter(siblings, request.AfterId);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE act SET position = @position, updated_at = now() WHERE id = @actId",
                new { position, actId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        return await PublishAsync(bookId, "act", cancellationToken);
    }

    public async Task<OutlineDto> MoveChapterAsync(Guid chapterId, MoveChapterRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Chapter, chapterId, BookRole.Write, cancellationToken, notFound: "Chapter not found.");
        Guid bookId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM chapter WHERE id = @chapterId AND archived_at IS NULL FOR UPDATE",
                new { chapterId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Chapter not found.");
            var target = await connection.QuerySingleOrDefaultAsync<Target>(new CommandDefinition(
                "SELECT book_id AS BookId, archived_at IS NOT NULL AS Archived FROM act WHERE id = @actId",
                new { actId = request.ActId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Target act not found.");
            RequireLive(target, bookId, "act");
            await SiblingKeys.LockAsync(connection, transaction, request.ActId, cancellationToken);
            var siblings = await SiblingKeys.SiblingsAsync(connection, transaction,
                "SELECT id, position FROM chapter WHERE act_id = @parentId AND archived_at IS NULL AND id <> @movingId ORDER BY position, id",
                request.ActId, chapterId, cancellationToken);
            var position = SiblingKeys.KeyAfter(siblings, request.AfterId);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE chapter SET act_id = @actId, position = @position, updated_at = now() WHERE id = @chapterId",
                new { actId = request.ActId, position, chapterId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        return await PublishAsync(bookId, "chapter", cancellationToken);
    }

    public async Task<OutlineDto> MoveSceneAsync(Guid sceneId, MoveSceneRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, notFound: "Scene not found.");
        Guid bookId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM scene WHERE id = @sceneId AND archived_at IS NULL FOR UPDATE",
                new { sceneId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Scene not found.");
            var target = await connection.QuerySingleOrDefaultAsync<Target>(new CommandDefinition(
                """
                SELECT c.book_id AS BookId, (c.archived_at IS NOT NULL OR a.archived_at IS NOT NULL) AS Archived
                FROM chapter c JOIN act a ON a.id = c.act_id WHERE c.id = @chapterId
                """,
                new { chapterId = request.ChapterId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Target chapter not found.");
            RequireLive(target, bookId, "chapter");
            await SiblingKeys.LockAsync(connection, transaction, request.ChapterId, cancellationToken);
            var siblings = await SiblingKeys.SiblingsAsync(connection, transaction,
                "SELECT id, position FROM scene WHERE chapter_id = @parentId AND archived_at IS NULL AND id <> @movingId ORDER BY position, id",
                request.ChapterId, sceneId, cancellationToken);
            var position = SiblingKeys.KeyAfter(siblings, request.AfterId);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scene SET chapter_id = @chapterId, position = @position, updated_at = now() WHERE id = @sceneId",
                new { chapterId = request.ChapterId, position, sceneId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        return await PublishAsync(bookId, "scene", cancellationToken);
    }

    private async Task<OutlineDto> PublishAsync(Guid bookId, string kind, CancellationToken cancellationToken)
    {
        await events.PublishAsync(new StructureChanged(bookId, kind));
        return await outline.GetAsync(bookId, cancellationToken);
    }

    private static void RequireLive(Target target, Guid bookId, string what)
    {
        if (target.BookId != bookId)
        {
            throw ApiException.Validation($"The target {what} belongs to another book.");
        }
        if (target.Archived)
        {
            throw ApiException.Validation($"The target {what} is archived.");
        }
    }
}
