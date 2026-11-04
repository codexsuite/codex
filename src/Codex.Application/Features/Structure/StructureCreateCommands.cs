using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Adds a chapter to an act or a scene to a chapter. The new row is placed by a fractional key between its neighbours
// and answered as the outline shows it (numbered).
[RegisterSingleton]
public sealed class StructureCreateCommands(NpgsqlDataSource dataSource, IBookAccess access, OutlineQuery outline, IBookEvents events)
{
    private const int MaxTitleLength = 200;

    private const string DefaultChapterTitle = "New chapter";

    public async Task<OutlineChapterDto> CreateChapterAsync(Guid actId, CreateChapterRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Act, actId, BookRole.Write, cancellationToken, notFound: "Act not found.");
        var title = request.Title?.Trim();
        if (title is { Length: > MaxTitleLength })
        {
            throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.");
        }

        Guid bookId;
        Guid chapterId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM act WHERE id = @actId AND archived_at IS NULL FOR UPDATE",
                new { actId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Act not found.");
            await SiblingKeys.LockAsync(connection, transaction, actId, cancellationToken);
            var siblings = await SiblingKeys.SiblingsAsync(connection, transaction,
                "SELECT id, position FROM chapter WHERE act_id = @parentId AND archived_at IS NULL AND id <> @movingId ORDER BY position, id",
                actId, Guid.Empty, cancellationToken);
            var position = KeyForNew(siblings, request.AfterId, "chapter");
            chapterId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO chapter (book_id, act_id, position, title) VALUES (@bookId, @actId, @position, @title) RETURNING id",
                new { bookId, actId, position, title = string.IsNullOrEmpty(title) ? DefaultChapterTitle : title },
                transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "chapter"));
        return (await outline.GetAsync(bookId, cancellationToken)).Acts
            .SelectMany(a => a.Chapters).Single(c => c.Id == chapterId);
    }

    public async Task<OutlineSceneDto> CreateSceneAsync(Guid chapterId, CreateSceneRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Chapter, chapterId, BookRole.Write, cancellationToken, notFound: "Chapter not found.");
        var title = request.Title?.Trim();
        if (title is { Length: > MaxTitleLength })
        {
            throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.");
        }

        Guid bookId;
        Guid sceneId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                """
                SELECT c.book_id FROM chapter c JOIN act a ON a.id = c.act_id
                WHERE c.id = @chapterId AND c.archived_at IS NULL AND a.archived_at IS NULL FOR UPDATE OF c
                """,
                new { chapterId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Chapter not found.");
            await SiblingKeys.LockAsync(connection, transaction, chapterId, cancellationToken);
            var siblings = await SiblingKeys.SiblingsAsync(connection, transaction,
                "SELECT id, position FROM scene WHERE chapter_id = @parentId AND archived_at IS NULL AND id <> @movingId ORDER BY position, id",
                chapterId, Guid.Empty, cancellationToken);
            var position = KeyForNew(siblings, request.AfterId, "scene");
            sceneId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO scene (book_id, chapter_id, position, title) VALUES (@bookId, @chapterId, @position, nullif(@title::text, '')) RETURNING id",
                new { bookId, chapterId, position, title }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "scene"));
        return await outline.GetSceneAsync(sceneId, cancellationToken);
    }

    // No AfterId appends; otherwise the key sits right after that sibling.
    private static string KeyForNew(List<Sibling> siblings, Guid? afterId, string what)
    {
        if (afterId == null)
        {
            return FractionalKey.Between(siblings.Count > 0 ? siblings[^1].Position : null, null);
        }
        if (siblings.All(s => s.Id != afterId))
        {
            throw ApiException.Validation($"afterId must be a live {what} in the same parent.");
        }
        return SiblingKeys.KeyAfter(siblings, afterId);
    }
}
