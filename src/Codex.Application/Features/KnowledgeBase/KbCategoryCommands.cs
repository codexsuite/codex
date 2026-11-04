using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.KnowledgeBase;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbCategoryCommands
{
    Task<KbCategoryDto> PatchAsync(Guid categoryId, PatchKbCategoryRequest request, CancellationToken cancellationToken);

    Task<KbCategoryDeleteImpactDto> ImpactAsync(Guid categoryId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid categoryId, CancellationToken cancellationToken);

    // Deletes the category (a soft delete: archived_at) after moving its live entries, in their order, to the end of another
    // category of the same book or series (moveToId null moves nothing, so live entries are a conflict). Returns the moved
    // entry ids in that order.
    Task<IReadOnlyList<Guid>> DeleteMovingAsync(Guid categoryId, Guid? moveToId, CancellationToken cancellationToken);

    Task ReorderAsync(Guid categoryId, MoveKbCategoryRequest request, CancellationToken cancellationToken);

    // Brings a deleted category back (clears archived_at), in its place. A live category with its name by now is 409 conflict.
    Task<KbCategoryDto> UnarchiveAsync(Guid categoryId, CancellationToken cancellationToken);
}

// Rename, recolour and delete a Knowledge base category. A category can only be deleted when no live entry is left in
// it (409 conflict: "Move or delete its N entries first."). Delete is soft: the category keeps its row with archived_at
// set, and the entries that were deleted and are only kept for Undo stay archived in it. Reorder resequences the owner's
// live categories as zero-padded keys (like entry positions); unarchive brings a deleted category back for MCP undo. A deleted
// category reads as unknown (404) everywhere else. A name the owning book or series already has is 409 conflict (case-insensitive); blank or over-long name,
// unknown colour 400 validation; unknown category 404. StructureChanged(owner, "kb") goes out after each write.
[RegisterSingleton]
public sealed class KbCategoryCommands(NpgsqlDataSource dataSource, IKbEntryQueries queries, IBookEvents events, IBookAccess access)
    : IKbCategoryCommands
{
    private const int MaxNameLength = 200;

    internal static readonly HashSet<string> Colours =
        ["category-blog", "category-law", "category-story", "category-research", "category-person"];

    private sealed record CategoryRow(Guid BookId, string Name, string? Tag);

    public async Task<KbCategoryDto> PatchAsync(Guid categoryId, PatchKbCategoryRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbCategory, categoryId, BookRole.Write, cancellationToken);
        if (request.Name == null && request.Colour == null)
        {
            throw ApiException.Validation("Nothing to change.");
        }
        var name = request.Name == null ? null : request.Name.Trim();
        if (name != null && (name.Length == 0 || name.Length > MaxNameLength))
        {
            throw ApiException.Validation($"Category name must be 1 to {MaxNameLength} characters.");
        }
        if (request.Colour != null && !Colours.Contains(request.Colour))
        {
            throw ApiException.Validation($"Colour must be one of {string.Join(", ", Colours.Order())}.");
        }

        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var category = await connection.QuerySingleOrDefaultAsync<CategoryRow>(new CommandDefinition(
                "SELECT book_id AS BookId, name, tag FROM kb_category WHERE id = @categoryId AND archived_at IS NULL FOR UPDATE",
                new { categoryId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Knowledge base category {categoryId} was not found.");
            ownerId = category.BookId;
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtextextended('kb_category:' || @ownerId::text, 0))",
                new { ownerId }, transaction, cancellationToken: cancellationToken));
            if (name != null && await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM kb_category WHERE book_id = @ownerId AND id <> @categoryId AND archived_at IS NULL AND lower(name) = lower(@name))",
                    new { ownerId, categoryId, name }, transaction, cancellationToken: cancellationToken)))
            {
                throw ApiException.Conflict($"The Knowledge base already has a category named {name}.");
            }
            // The short tag follows the name unless someone set it to something else.
            var retag = name != null && (category.Tag == null || string.Equals(category.Tag, category.Name, StringComparison.OrdinalIgnoreCase));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE kb_category SET
                    name = coalesce(@name::text, name),
                    tag = CASE WHEN @retag THEN lower(@name::text) ELSE tag END,
                    color = coalesce(@colour::text, color),
                    updated_at = now()
                WHERE id = @categoryId
                """,
                new { categoryId, name, retag, colour = request.Colour }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return await queries.GetCategoryAsync(ownerId, categoryId, cancellationToken);
    }

    public async Task<KbCategoryDeleteImpactDto> ImpactAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbCategory, categoryId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var entries = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM kb_category WHERE id = @categoryId AND archived_at IS NULL)
                        THEN (SELECT count(*)::integer FROM kb_entry WHERE category_id = @categoryId AND archived_at IS NULL) END
            """,
            new { categoryId }, cancellationToken: cancellationToken));
        return entries == null
            ? throw ApiException.NotFound($"Knowledge base category {categoryId} was not found.")
            : new KbCategoryDeleteImpactDto(entries.Value);
    }

    public async Task DeleteAsync(Guid categoryId, CancellationToken cancellationToken) =>
        await DeleteMovingAsync(categoryId, null, cancellationToken);

    public async Task<IReadOnlyList<Guid>> DeleteMovingAsync(Guid categoryId, Guid? moveToId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbCategory, categoryId, BookRole.Write, cancellationToken);
        if (moveToId != null)
        {
            await access.RequireAsync(BookItem.KbCategory, moveToId.Value, BookRole.Write, cancellationToken);
        }
        Guid ownerId;
        List<Guid> moved;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            ownerId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM kb_category WHERE id = @categoryId AND archived_at IS NULL FOR UPDATE", new { categoryId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Knowledge base category {categoryId} was not found.");
            moved = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT id FROM kb_entry WHERE category_id = @categoryId AND archived_at IS NULL
                ORDER BY position COLLATE "C", id FOR UPDATE
                """,
                new { categoryId }, transaction, cancellationToken: cancellationToken))).ToList();
            if (moved.Count > 0 && moveToId == null)
            {
                throw ApiException.Conflict($"Move or delete its {moved.Count} {(moved.Count == 1 ? "entry" : "entries")} first.");
            }
            if (moved.Count > 0)
            {
                await MoveEntriesAsync(connection, transaction, ownerId, moved, moveToId!.Value, cancellationToken);
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE kb_category SET archived_at = now(), updated_at = now() WHERE id = @categoryId",
                new { categoryId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return moved;
    }

    // The entries go after the target's last live entry, in order: a key that extends the largest position sorts after
    // every existing one, and the zero-padded counter keeps the moved entries' order.
    private static async Task MoveEntriesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid ownerId, List<Guid> ids, Guid targetId, CancellationToken cancellationToken)
    {
        if (await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM kb_category WHERE id = @targetId AND archived_at IS NULL", new { targetId }, transaction, cancellationToken: cancellationToken)) != ownerId)
        {
            throw ApiException.Validation("The category to move the entries to must belong to the same book or series.");
        }
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@targetId::text, 0))", new { targetId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE kb_entry e SET category_id = @targetId,
                position = coalesce((SELECT max(position COLLATE "C") FROM kb_entry WHERE category_id = @targetId AND archived_at IS NULL), 'a')
                           || 'z' || lpad(t.ord::text, 5, '0')
            FROM unnest(@ids) WITH ORDINALITY AS t (id, ord)
            WHERE e.id = t.id
            """,
            new { targetId, ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));
    }

    public async Task ReorderAsync(Guid categoryId, MoveKbCategoryRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbCategory, categoryId, BookRole.Write, cancellationToken);
        if (request.AfterId == categoryId)
        {
            throw ApiException.Validation("A category cannot be placed after itself.");
        }
        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            ownerId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM kb_category WHERE id = @categoryId AND archived_at IS NULL FOR UPDATE", new { categoryId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Knowledge base category {categoryId} was not found.");
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtextextended('kb_category:' || @ownerId::text, 0))",
                new { ownerId }, transaction, cancellationToken: cancellationToken));
            var ids = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT id FROM kb_category WHERE book_id = @ownerId AND id <> @categoryId AND archived_at IS NULL ORDER BY position COLLATE \"C\", id",
                new { ownerId, categoryId }, transaction, cancellationToken: cancellationToken))).ToList();
            var index = request.AfterId == null ? -1 : ids.IndexOf(request.AfterId.Value);
            if (request.AfterId != null && index < 0)
            {
                throw ApiException.Validation("afterId must be another category of the same book or series.");
            }
            ids.Insert(index + 1, categoryId);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE kb_category c SET position = 'a' || lpad(t.ord::text, 5, '0'), updated_at = now()
                FROM unnest(@ids) WITH ORDINALITY AS t (id, ord)
                WHERE c.id = t.id AND c.position IS DISTINCT FROM 'a' || lpad(t.ord::text, 5, '0')
                """,
                new { ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
    }

    public async Task<KbCategoryDto> UnarchiveAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbCategory, categoryId, BookRole.Write, cancellationToken);
        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var category = await connection.QuerySingleOrDefaultAsync<CategoryRow>(new CommandDefinition(
                "SELECT book_id AS BookId, name, tag FROM kb_category WHERE id = @categoryId AND archived_at IS NOT NULL FOR UPDATE",
                new { categoryId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Deleted Knowledge base category {categoryId} was not found.");
            ownerId = category.BookId;
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtextextended('kb_category:' || @ownerId::text, 0))",
                new { ownerId }, transaction, cancellationToken: cancellationToken));
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM kb_category WHERE book_id = @ownerId AND archived_at IS NULL AND lower(name) = lower(@name))",
                    new { ownerId, name = category.Name }, transaction, cancellationToken: cancellationToken)))
            {
                throw ApiException.Conflict($"The Knowledge base already has a category named {category.Name}.");
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE kb_category SET archived_at = NULL, updated_at = now() WHERE id = @categoryId",
                new { categoryId }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return await queries.GetCategoryAsync(ownerId, categoryId, cancellationToken);
    }
}
