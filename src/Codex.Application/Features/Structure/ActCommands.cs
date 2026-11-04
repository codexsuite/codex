using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Structure;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Adds, renames, archives ("Delete act") and restores an act. A new act starts with one chapter holding one empty
// scene, so the board has a card to type into. A book keeps at least one live act. Each command checks that the current
// user owns the book first (#29): another user's act is not_found.
[RegisterSingleton]
public sealed class ActCommands(NpgsqlDataSource dataSource, IBookAccess access, OutlineQuery outline, IBookEvents events)
{
    private sealed record ActRow(Guid BookId, string Position, bool Archived);

    private const int MaxTitleLength = 200;

    private const string DefaultChapterTitle = "New chapter";

    public async Task<OutlineActDto> CreateAsync(Guid bookId, CreateActRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken, notFound: "Book not found.");
        var title = CheckTitle(request.Title);

        Guid actId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var live = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book' AND archived_at IS NULL)",
                new { bookId }, transaction, cancellationToken: cancellationToken));
            if (!live)
            {
                throw ApiException.NotFound("Book not found.");
            }
            await SiblingKeys.LockAsync(connection, transaction, bookId, cancellationToken);
            var siblings = await SiblingKeys.SiblingsAsync(connection, transaction,
                "SELECT id, position FROM act WHERE book_id = @parentId AND archived_at IS NULL AND id <> @movingId ORDER BY position, id",
                bookId, Guid.Empty, cancellationToken);
            var position = KeyForNew(siblings, request.AfterId);
            actId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO act (book_id, position, title) VALUES (@bookId, @position, @title) RETURNING id",
                new { bookId, position, title }, transaction, cancellationToken: cancellationToken));
            var chapterId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO chapter (book_id, act_id, position, title) VALUES (@bookId, @actId, @position, @title) RETURNING id",
                new { bookId, actId, position = FractionalKey.Between(null, null), title = DefaultChapterTitle },
                transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO scene (book_id, chapter_id, position) VALUES (@bookId, @chapterId, @position)",
                new { bookId, chapterId, position = FractionalKey.Between(null, null) },
                transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "act"));
        return await ActAsync(bookId, actId, cancellationToken);
    }

    public async Task<OutlineActDto> RenameAsync(Guid actId, PatchActRequest request, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Act, actId, BookRole.Write, cancellationToken, notFound: "Act not found.");
        var title = CheckTitle(request.Title ?? throw ApiException.Validation("title is required."));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "UPDATE act SET title = @title, updated_at = now() WHERE id = @actId AND archived_at IS NULL RETURNING book_id",
            new { actId, title }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Act not found.");

        await events.PublishAsync(new StructureChanged(bookId, "act"));
        return await ActAsync(bookId, actId, cancellationToken);
    }

    // The act's chapters and scenes keep their own archived_at, so Restore brings back exactly what was live.
    public async Task<ArchiveActResult> ArchiveAsync(Guid actId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Act, actId, BookRole.Write, cancellationToken, notFound: "Act not found.");
        Guid bookId;
        List<SceneUseDto> uses;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var act = await LockActAsync(connection, transaction, actId, cancellationToken);
            if (act.Archived)
            {
                throw ApiException.Conflict("The act is already deleted.");
            }
            bookId = act.BookId;
            await SiblingKeys.LockAsync(connection, transaction, bookId, cancellationToken);
            var others = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM act WHERE book_id = @bookId AND archived_at IS NULL AND id <> @actId)",
                new { bookId, actId }, transaction, cancellationToken: cancellationToken));
            if (!others)
            {
                throw ApiException.Conflict("A book needs at least one act.");
            }
            // The uses the clearance loses are exactly those the reference outline shows for the act's scenes now.
            uses = (await connection.QueryAsync<SceneUseDto>(new CommandDefinition(
                """
                SELECT o.use_id AS UseId, o.reference_id AS SourceId, r.title AS SourceTitle
                FROM reference_use_outline o
                JOIN reference r ON r.id = o.reference_id
                JOIN scene s ON s.id = o.scene_id
                JOIN chapter c ON c.id = s.chapter_id
                WHERE c.act_id = @actId ORDER BY o.book_order
                """,
                new { actId }, transaction, cancellationToken: cancellationToken))).ToList();
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE act SET archived_at = now(), updated_at = now() WHERE id = @actId",
                new { actId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "act"));
        return new ArchiveActResult(actId, uses);
    }

    public async Task<OutlineActDto> RestoreAsync(Guid actId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Act, actId, BookRole.Write, cancellationToken, notFound: "Act not found.");
        Guid bookId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var act = await LockActAsync(connection, transaction, actId, cancellationToken);
            if (!act.Archived)
            {
                throw ApiException.Conflict("The act is not deleted.");
            }
            bookId = act.BookId;
            await SiblingKeys.LockAsync(connection, transaction, bookId, cancellationToken);
            // The act keeps its old key unless a live act took it meanwhile; then it goes right after that one.
            var live = (await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT position FROM act WHERE book_id = @bookId AND archived_at IS NULL AND position >= @position ORDER BY position LIMIT 2",
                new { bookId, act.Position }, transaction, cancellationToken: cancellationToken))).ToList();
            var position = act.Position;
            if (live.Count > 0 && live[0] == act.Position)
            {
                position = FractionalKey.Between(act.Position, live.Count > 1 ? live[1] : null);
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE act SET archived_at = NULL, position = @position, updated_at = now() WHERE id = @actId",
                new { actId, position }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }

        await events.PublishAsync(new StructureChanged(bookId, "act"));
        return await ActAsync(bookId, actId, cancellationToken);
    }

    // No AfterId appends; otherwise the key sits right after that act.
    private static string KeyForNew(List<Sibling> siblings, Guid? afterId)
    {
        if (afterId == null)
        {
            return FractionalKey.Between(siblings.Count > 0 ? siblings[^1].Position : null, null);
        }
        if (siblings.TrueForAll(s => s.Id != afterId))
        {
            throw ApiException.Validation("afterId must be a live act of the book.");
        }
        return SiblingKeys.KeyAfter(siblings, afterId);
    }

    private static string CheckTitle(string? title)
    {
        var trimmed = title?.Trim() ?? "";
        return trimmed.Length > MaxTitleLength
            ? throw ApiException.Validation($"Title can be at most {MaxTitleLength} characters.")
            : trimmed;
    }

    private static async Task<ActRow> LockActAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actId, CancellationToken cancellationToken)
        => await connection.QuerySingleOrDefaultAsync<ActRow>(new CommandDefinition(
            """
            SELECT a.book_id AS BookId, a.position, a.archived_at IS NOT NULL AS Archived
            FROM act a JOIN book b ON b.id = a.book_id
            WHERE a.id = @actId AND b.archived_at IS NULL FOR UPDATE OF a
            """,
            new { actId }, transaction, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Act not found.");

    // The act as the outline shows it, chapters numbered.
    private async Task<OutlineActDto> ActAsync(Guid bookId, Guid actId, CancellationToken cancellationToken)
        => (await outline.GetAsync(bookId, cancellationToken)).Acts.Single(a => a.Id == actId);
}
