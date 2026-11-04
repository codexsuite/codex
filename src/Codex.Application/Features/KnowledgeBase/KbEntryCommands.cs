using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.KnowledgeBase;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbEntryCommands
{
    Task<KbEntryDto> CreateAsync(Guid bookId, CreateKbEntryRequest request, CancellationToken cancellationToken);

    Task<KbEntryDto> PatchAsync(Guid entryId, PatchKbEntryRequest request, CancellationToken cancellationToken);

    Task MoveAsync(Guid entryId, MoveKbEntryRequest request, CancellationToken cancellationToken);

    Task<KbCategoryDto> CreateCategoryAsync(Guid bookId, CreateKbCategoryRequest request, CancellationToken cancellationToken);
}

// Writes to kb_entry and kb_category. Each write publishes StructureChanged(book, "kb") under the owning book or
// series id. Errors: unknown book or entry 404, blank or over-long text, unknown field key, a category outside the
// book's scope 400 validation, an alias another entry in scope already uses (name or alias) 409 conflict.
[RegisterSingleton]
public sealed class KbEntryCommands(NpgsqlDataSource dataSource, IKbEntryQueries queries, IBookEvents events, IBookAccess access) : IKbEntryCommands
{
    private const int MaxNameLength = 200;

    private sealed record CategoryRef(Guid Id, Guid BookId);

    private sealed record EntryRef(Guid BookId, Guid CategoryId, string Name, string Aliases);

    private static readonly HashSet<string> Colours =
        ["category-blog", "category-law", "category-story", "category-research", "category-person"];

    public async Task<KbEntryDto> CreateAsync(Guid bookId, CreateKbEntryRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        var name = RequireName(request.Name, "Name");
        Guid entryId;
        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await RequireBookAsync(connection, transaction, bookId, cancellationToken);
            if (request.AllowDuplicate != true)
            {
                var taken = await FindCollisionAsync(connection, transaction, Guid.Empty, bookId, name, cancellationToken);
                if (taken != null)
                {
                    throw ApiException.Conflict($"\"{name}\" is already a name or alias of {taken}.");
                }
            }
            await RequireLiveCategoryAsync(connection, transaction, request.CategoryId, cancellationToken);
            // A category of the book's scope; the book's own row wins over the series' same-named one.
            var category = await connection.QuerySingleOrDefaultAsync<CategoryRef>(new CommandDefinition(
                """
                SELECT coalesce(bc.own_id, bc.series_id) AS Id, c2.book_id AS BookId
                FROM kb_category c
                JOIN book_kb_category bc ON bc.book_id = @bookId AND bc.name = c.name
                JOIN kb_category c2 ON c2.id = coalesce(bc.own_id, bc.series_id)
                WHERE c.id = @categoryId AND c.id = ANY (bc.category_ids)
                """,
                new { bookId, categoryId = request.CategoryId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.Validation("The category does not belong to this book or its series.");
            ownerId = category.BookId;
            entryId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "INSERT INTO kb_entry (book_id, category_id, name, position) VALUES (@ownerId, @categoryId, @name, '~') RETURNING id",
                new { ownerId, categoryId = category.Id, name }, transaction, cancellationToken: cancellationToken));
            await ResequenceAsync(connection, transaction, category.Id, entryId, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return await queries.GetAsync(entryId, cancellationToken);
    }

    public async Task<KbEntryDto> PatchAsync(Guid entryId, PatchKbEntryRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.Write, cancellationToken);
        if (request.Name == null && request.Summary == null && request.Body == null && request.Fields == null
            && request.CustomFields == null && request.Aliases == null && request.AlwaysInContext == null && request.CategoryId == null
            && request.Tags == null && request.ExcludeFromAutoContext == null)
        {
            throw ApiException.Validation("Nothing to change.");
        }
        var name = request.Name == null ? null : RequireName(request.Name, "Name");
        var aliases = request.Aliases == null ? null : CleanAliases(request.Aliases);
        var tags = request.Tags == null ? null : CleanAliases(request.Tags);
        if (request.CustomFields != null && request.CustomFields.Any(f => string.IsNullOrWhiteSpace(f.Label)))
        {
            throw ApiException.Validation("Every custom field needs a label.");
        }

        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var entry = await connection.QuerySingleOrDefaultAsync<EntryRef>(new CommandDefinition(
                "SELECT book_id AS BookId, category_id AS CategoryId, name, to_json(aliases)::text AS Aliases FROM kb_entry WHERE id = @entryId AND archived_at IS NULL FOR UPDATE",
                new { entryId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
            ownerId = entry.BookId;

            if (request.Fields != null)
            {
                await RequireFieldKeysAsync(connection, transaction, request.CategoryId ?? entry.CategoryId, request.Fields.Keys, cancellationToken);
            }
            if (request.CategoryId != null)
            {
                await RequireLiveCategoryAsync(connection, transaction, request.CategoryId.Value, cancellationToken);
            }
            var allowDuplicate = request.AllowDuplicate == true;
            if (name != null && !allowDuplicate && !string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase))
            {
                var taken = await FindCollisionAsync(connection, transaction, entryId, entry.BookId, name, cancellationToken);
                if (taken != null)
                {
                    throw ApiException.Conflict($"\"{name}\" is already a name or alias of {taken}.");
                }
            }
            if (aliases != null)
            {
                await RequireNoCollisionAsync(connection, transaction, entryId, entry.BookId, name ?? entry.Name, aliases, allowDuplicate, cancellationToken);
            }
            aliases = await KeepOldNameAsync(
                connection, transaction, entryId, entry, name, aliases, request.KeepOldName != false, cancellationToken);

            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE kb_entry SET
                        name = coalesce(@name::text, name),
                        summary = CASE WHEN @summary::text IS NULL THEN summary ELSE nullif(btrim(@summary::text), '') END,
                        body = coalesce(@body::text, body),
                        fields = coalesce(@fields::text::jsonb, fields),
                        custom_fields = coalesce(@customFields::text::jsonb, custom_fields),
                        aliases = coalesce(@aliases::text[], aliases),
                        always_in_context = coalesce(@alwaysInContext::boolean, always_in_context),
                        tags = coalesce(@tags::text[], tags),
                        exclude_from_auto_context = coalesce(@excludeFromAutoContext::boolean, exclude_from_auto_context),
                        category_id = coalesce(@categoryId::uuid, category_id),
                        updated_at = now()
                    WHERE id = @entryId
                    """,
                    new
                    {
                        entryId,
                        name,
                        summary = request.Summary,
                        body = request.Body,
                        fields = request.Fields == null ? null : JsonSerializer.Serialize(request.Fields, JsonSerializerOptions.Web),
                        customFields = request.CustomFields == null ? null : JsonSerializer.Serialize(request.CustomFields, JsonSerializerOptions.Web),
                        aliases = aliases?.ToArray(),
                        alwaysInContext = request.AlwaysInContext,
                        tags = tags?.ToArray(),
                        excludeFromAutoContext = request.ExcludeFromAutoContext,
                        categoryId = request.CategoryId,
                    }, transaction, cancellationToken: cancellationToken));
                if (request.CategoryId != null && request.CategoryId != entry.CategoryId)
                {
                    await ResequenceAsync(connection, transaction, request.CategoryId.Value, entryId, null, cancellationToken);
                }
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                throw ApiException.Validation("The category must belong to the same book or series as the entry.");
            }
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return await queries.GetAsync(entryId, cancellationToken);
    }

    public async Task MoveAsync(Guid entryId, MoveKbEntryRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.Write, cancellationToken);
        Guid ownerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            var entry = await connection.QuerySingleOrDefaultAsync<EntryRef>(new CommandDefinition(
                "SELECT book_id AS BookId, category_id AS CategoryId, name, to_json(aliases)::text AS Aliases FROM kb_entry WHERE id = @entryId AND archived_at IS NULL FOR UPDATE",
                new { entryId }, transaction, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
            ownerId = entry.BookId;
            if (request.AfterId == entryId)
            {
                throw ApiException.Validation("An entry cannot be placed after itself.");
            }
            await ResequenceAsync(connection, transaction, entry.CategoryId, entryId, new Placement(request.AfterId), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
    }

    public async Task<KbCategoryDto> CreateCategoryAsync(Guid bookId, CreateKbCategoryRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.Write, cancellationToken);
        var name = RequireName(request.Name, "Category name");
        if (request.Colour == null || !Colours.Contains(request.Colour))
        {
            throw ApiException.Validation($"Colour must be one of {string.Join(", ", Colours.Order())}.");
        }

        Guid id;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await RequireBookAsync(connection, transaction, bookId, cancellationToken);
            // One category create per book at a time: the name check is case-insensitive (the unique constraint is
            // not) and the new position is max + 1, so two concurrent creates must not both pass the check.
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtextextended('kb_category:' || @bookId::text, 0))",
                new { bookId }, transaction, cancellationToken: cancellationToken));
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    """
                    SELECT EXISTS (SELECT 1 FROM book_kb_category WHERE book_id = @bookId AND lower(name) = lower(@name))
                    """,
                    new { bookId, name }, transaction, cancellationToken: cancellationToken)))
            {
                throw ApiException.Conflict($"The Knowledge base already has a category named {name}.");
            }
            id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO kb_category (book_id, name, tag, color, position)
                SELECT @bookId, @name, lower(@name), @colour,
                       coalesce((SELECT max(position COLLATE "C") FROM kb_category WHERE book_id = @bookId AND archived_at IS NULL), 'a') || 'a'
                RETURNING id
                """,
                new { bookId, name, colour = request.Colour }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(bookId, "kb"));
        return await queries.GetCategoryAsync(bookId, id, cancellationToken);
    }

    private static string RequireName(string? value, string what)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw ApiException.Validation($"{what} cannot be blank.");
        }
        return trimmed.Length > MaxNameLength
            ? throw ApiException.Validation($"{what} can be at most {MaxNameLength} characters.")
            : trimmed;
    }

    private static List<string> CleanAliases(IReadOnlyList<string> aliases) =>
        aliases.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static async Task RequireBookAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid bookId, CancellationToken cancellationToken)
    {
        if (!await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId)", new { bookId }, transaction, cancellationToken: cancellationToken)))
        {
            throw ApiException.NotFound($"Book {bookId} was not found.");
        }
    }

    // A deleted category reads as unknown. An id that is no category at all is left to the scope check (validation).
    private static async Task RequireLiveCategoryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid categoryId, CancellationToken cancellationToken)
    {
        if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM kb_category WHERE id = @categoryId AND archived_at IS NOT NULL)",
                new { categoryId }, transaction, cancellationToken: cancellationToken)))
        {
            throw ApiException.NotFound($"Knowledge base category {categoryId} was not found.");
        }
    }

    private static async Task RequireFieldKeysAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid categoryId, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var defined = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT field_defs::text FROM kb_category WHERE id = @categoryId", new { categoryId }, transaction, cancellationToken: cancellationToken));
        var allowed = (defined == null ? [] : JsonSerializer.Deserialize<List<KbFieldDefDto>>(defined, JsonSerializerOptions.Web) ?? [])
            .Select(d => d.Key).ToHashSet();
        var unknown = keys.FirstOrDefault(k => !allowed.Contains(k));
        if (unknown != null)
        {
            throw ApiException.Validation($"The category has no field {unknown}.");
        }
    }

    private static async Task RequireNoCollisionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid entryId, Guid bookId, string ownName,
        IReadOnlyList<string> aliases, bool allowDuplicate, CancellationToken cancellationToken)
    {
        foreach (var alias in aliases)
        {
            if (string.Equals(alias, ownName, StringComparison.OrdinalIgnoreCase))
            {
                throw ApiException.Validation($"{alias} is the entry's own name; an alias must be a different name.");
            }
            if (allowDuplicate)
            {
                continue;
            }
            var other = await FindCollisionAsync(connection, transaction, entryId, bookId, alias, cancellationToken);
            if (other != null)
            {
                throw ApiException.Conflict($"\"{alias}\" is already a name or alias of {other}.");
            }
        }
    }

    private static Task<string?> FindCollisionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid entryId, Guid bookId, string alias, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            SELECT e.name FROM kb_entry e
            WHERE e.archived_at IS NULL AND e.id <> @entryId
              AND e.book_id IN (SELECT scope_id FROM book_scope WHERE book_id = @bookId)
              AND (lower(e.name) = lower(@alias) OR EXISTS (SELECT 1 FROM unnest(e.aliases) a WHERE lower(a) = lower(@alias)))
            ORDER BY e.name LIMIT 1
            """,
            new { entryId, bookId, alias }, transaction, cancellationToken: cancellationToken));

    // Rename keeps the old name as an alias, so text that used it still counts as this entry, unless another entry in
    // scope already answers to it. An alias equal to the new name is dropped (a name is not its own alias).
    // Returns the alias list to store, or null to leave the aliases as they are.
    private static async Task<List<string>?> KeepOldNameAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid entryId, EntryRef entry, string? newName, List<string>? requested,
        bool keepOldName, CancellationToken cancellationToken)
    {
        if (newName == null || string.Equals(newName, entry.Name, StringComparison.Ordinal))
        {
            return requested;
        }
        var current = requested ?? JsonSerializer.Deserialize<List<string>>(entry.Aliases) ?? [];
        var result = current.Where(a => !string.Equals(a, newName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (keepOldName && !string.Equals(entry.Name, newName, StringComparison.OrdinalIgnoreCase)
            && !result.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)
            && await FindCollisionAsync(connection, transaction, entryId, entry.BookId, entry.Name, cancellationToken) == null)
        {
            result.Add(entry.Name);
        }
        return requested == null && result.SequenceEqual(current) ? null : result;
    }

    // Where a moved entry goes: first in its category when AfterId is null, else right after that entry.
    private sealed record Placement(Guid? AfterId);

    // Rewrites the positions of a category's live entries as zero-padded keys in order. `entryId` is put in place
    // first: at `placement`, or last when there is none (a new entry, or one that changed category).
    private static async Task ResequenceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid categoryId, Guid entryId, Placement? placement,
        CancellationToken cancellationToken)
    {
        // One resequence per category at a time: two concurrent creates would otherwise both number from the same snapshot.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@categoryId::text, 0))",
            new { categoryId }, transaction, cancellationToken: cancellationToken));
        var ids = (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT id FROM kb_entry WHERE category_id = @categoryId AND archived_at IS NULL AND id <> @entryId
            ORDER BY position COLLATE "C", id
            """,
            new { categoryId, entryId }, transaction, cancellationToken: cancellationToken))).ToList();
        var index = ids.Count - 1;
        if (placement != null)
        {
            index = placement.AfterId == null ? -1 : ids.IndexOf(placement.AfterId.Value);
            if (placement.AfterId != null && index < 0)
            {
                throw ApiException.Validation("afterId must be another entry of the same category.");
            }
        }
        ids.Insert(index + 1, entryId);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE kb_entry e SET position = 'a' || lpad(t.ord::text, 5, '0')
            FROM unnest(@ids) WITH ORDINALITY AS t (id, ord)
            WHERE e.id = t.id AND e.position IS DISTINCT FROM 'a' || lpad(t.ord::text, 5, '0')
            """,
            new { ids = ids.ToArray() }, transaction, cancellationToken: cancellationToken));
    }
}
