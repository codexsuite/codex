using System.Data.Common;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbPromoteCommands
{
    Task<KbEntryDto> PromoteAsync(Guid entryId, bool merge, CancellationToken cancellationToken);
}

// "Move to series": a book's entry becomes the series' entry, so every book in the series sees it. Without a clash this
// is one UPDATE of book_id (and the category, found or created by name in the series); the id stays, so mentions, links,
// notes and Used-in keep working. When the series already has a live entry with that name (or slug) the call answers
// needs_confirmation (409) unless merge is true: then the book's entry is folded into the series one (its scene and beat
// links, notes, sources, related links and mentions are repointed; its name and aliases become aliases of the series
// entry) and archived, its links with it. Scene text changes go through IProseStore, the rest in the same transaction.
[RegisterSingleton]
public sealed class KbPromoteCommands(NpgsqlDataSource dataSource, IProseStore proseStore, IKbEntryQueries queries, IBookEvents events, IBookAccess access)
    : IKbPromoteCommands
{
    private const string MergeOrigin = "kb_merge";

    // Aliases travel as JSON text: Dapper cannot bind a text[] column to a record constructor.
    private sealed record Own(Guid BookId, Guid CategoryId, string Name, string AliasesJson, string Slug, string Kind, Guid? SeriesId, string CategoryName)
    {
        public IReadOnlyList<string> Aliases => System.Text.Json.JsonSerializer.Deserialize<List<string>>(AliasesJson) ?? [];
    }

    private sealed record Clash(Guid Id, bool Archived, bool SameName);

    public async Task<KbEntryDto> PromoteAsync(Guid entryId, bool merge, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.Write, cancellationToken);
        Own own;
        Clash? clash;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            own = await ReadOwnAsync(connection, entryId, cancellationToken);
            clash = await FindClashAsync(connection, own, cancellationToken);
        }
        // The entry lands in the series, which every book in it reads, so this needs write on the series as well as
        // on the book (#80): a grant on the book alone gives only view there.
        await access.RequireAsync(own.SeriesId!.Value, BookRole.Write, cancellationToken);
        if (clash is { Archived: true })
        {
            throw ApiException.Conflict($"A deleted series entry already uses the name {own.Name}. Restore or rename it first.");
        }
        if (clash != null && !merge)
        {
            throw ApiException.NeedsConfirmation($"The series already has an entry named {own.Name}. Merge into the series entry, or cancel.");
        }

        if (clash == null)
        {
            await PromoteAsync(entryId, own, cancellationToken);
        }
        else
        {
            await MergeAsync(entryId, clash.Id, own, cancellationToken);
        }
        await events.PublishAsync(new StructureChanged(own.BookId, "kb"));
        await events.PublishAsync(new StructureChanged(own.SeriesId.Value, "kb"));
        return await queries.GetAsync(clash?.Id ?? entryId, cancellationToken);
    }

    private static async Task<Own> ReadOwnAsync(NpgsqlConnection connection, Guid entryId, CancellationToken cancellationToken)
    {
        var own = await connection.QuerySingleOrDefaultAsync<Own>(new CommandDefinition(
            """
            SELECT e.book_id AS BookId, e.category_id AS CategoryId, e.name, to_json(e.aliases)::text AS AliasesJson, e.slug, b.kind, b.series_id AS SeriesId,
                   c.name AS CategoryName
            FROM kb_entry e JOIN book b ON b.id = e.book_id JOIN kb_category c ON c.id = e.category_id
            WHERE e.id = @entryId AND e.archived_at IS NULL
            """,
            new { entryId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
        if (own.Kind == "series")
        {
            throw ApiException.Conflict("That entry already belongs to the series.");
        }
        if (own.SeriesId == null)
        {
            throw ApiException.Validation("Only a book in a series can share an entry with it.");
        }
        return own;
    }

    private static Task<Clash?> FindClashAsync(NpgsqlConnection connection, Own own, CancellationToken cancellationToken) =>
        connection.QueryFirstOrDefaultAsync<Clash>(new CommandDefinition(
            """
            SELECT id, (archived_at IS NOT NULL) AS Archived, (lower(name) = lower(@name)) AS SameName
            FROM kb_entry WHERE book_id = @series AND (lower(name) = lower(@name) OR slug = @slug)
            ORDER BY (archived_at IS NOT NULL), (lower(name) = lower(@name)) DESC LIMIT 1
            """,
            new { series = own.SeriesId, name = own.Name, slug = own.Slug }, cancellationToken: cancellationToken))!;

    private async Task PromoteAsync(Guid entryId, Own own, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO kb_category (book_id, name, tag, color, icon, position, field_defs)
            SELECT @series, name, tag, color, icon, position, field_defs FROM kb_category WHERE id = @category
            ON CONFLICT (book_id, name) WHERE archived_at IS NULL DO NOTHING
            """,
            new { series = own.SeriesId, category = own.CategoryId }, transaction, cancellationToken: cancellationToken));
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE kb_entry SET book_id = @series,
                   category_id = (SELECT id FROM kb_category WHERE book_id = @series AND name = @categoryName AND archived_at IS NULL)
            WHERE id = @entryId AND book_id = @bookId AND archived_at IS NULL
            """,
            new { entryId, series = own.SeriesId, bookId = own.BookId, categoryName = own.CategoryName }, transaction, cancellationToken: cancellationToken));
        if (updated == 0)
        {
            throw ApiException.Conflict("That entry changed. Reload and try again.");
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task MergeAsync(Guid entryId, Guid targetId, Own own, CancellationToken cancellationToken)
    {
        var source = entryId.ToString();
        var target = targetId.ToString();
        Guid[] sceneIds;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            sceneIds = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT p.scene_id FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @bookId AND strpos(lower(p.body), @needle) > 0",
                new { bookId = own.BookId, needle = source }, cancellationToken: cancellationToken))).ToArray();
        }

        var edits = new List<(Guid sceneId, int expectedVersion, Func<string, string> edit)>();
        foreach (var sceneId in sceneIds)
        {
            var version = (await proseStore.GetAsync(sceneId, cancellationToken)).Version;
            edits.Add((sceneId, version, body => body.Replace(source, target, StringComparison.OrdinalIgnoreCase)));
        }
        await proseStore.UpdateManyAsync(edits, MergeOrigin, new ProseWriteOptions
        {
            InTransaction = (connection, transaction, ct) => FoldAsync(connection, transaction, entryId, targetId, own, ct),
        }, cancellationToken);
    }

    // Everything but the scene bodies, in the prose write's transaction.
    private static async Task FoldAsync(
        DbConnection connection, DbTransaction transaction, Guid entryId, Guid targetId, Own own, CancellationToken cancellationToken)
    {
        var args = new { entryId, targetId, bookId = own.BookId, names = new[] { own.Name }.Concat(own.Aliases).ToArray() };
        var mapArgs = new { map = $$"""{"{{entryId}}":"{{targetId}}"}""", bookId = own.BookId };
        string[] statements =
        [
            """
            INSERT INTO scene_kb (scene_id, kb_entry_id) SELECT scene_id, @targetId FROM scene_kb WHERE kb_entry_id = @entryId AND archived_at IS NULL
            ON CONFLICT (scene_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """,
            "UPDATE scene_kb SET archived_at = now() WHERE kb_entry_id = @entryId AND archived_at IS NULL",
            """
            INSERT INTO beat_kb (beat_id, kb_entry_id) SELECT beat_id, @targetId FROM beat_kb WHERE kb_entry_id = @entryId AND archived_at IS NULL
            ON CONFLICT (beat_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """,
            "UPDATE beat_kb SET archived_at = now() WHERE kb_entry_id = @entryId AND archived_at IS NULL",
            "UPDATE kb_progression SET kb_entry_id = @targetId WHERE kb_entry_id = @entryId",
            "UPDATE reference SET kb_entry_id = @targetId WHERE kb_entry_id = @entryId",
            """
            INSERT INTO kb_entry_link (entry_a_id, entry_b_id, created_at)
            SELECT least(o.other, @targetId), greatest(o.other, @targetId), o.created_at
            FROM (SELECT CASE WHEN entry_a_id = @entryId THEN entry_b_id ELSE entry_a_id END AS other, created_at
                  FROM kb_entry_link WHERE (entry_a_id = @entryId OR entry_b_id = @entryId) AND archived_at IS NULL) o
            WHERE o.other <> @targetId
            ON CONFLICT (entry_a_id, entry_b_id) DO UPDATE SET archived_at = NULL
            """,
            "UPDATE kb_entry_link SET archived_at = now() WHERE (entry_a_id = @entryId OR entry_b_id = @entryId) AND archived_at IS NULL",
            """
            UPDATE kb_entry t SET aliases = coalesce((
                SELECT array_agg(DISTINCT a) FROM unnest(t.aliases || @names) AS a WHERE lower(a) <> lower(t.name)), '{}')
            WHERE t.id = @targetId
            """,
        ];
        foreach (var sql in statements)
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, args, transaction, cancellationToken: cancellationToken));
        }

        string[] remaps =
        [
            "UPDATE scene SET summary = remap_uuids(summary, @map::jsonb), plan_notes = remap_uuids(plan_notes, @map::jsonb) WHERE book_id = @bookId",
            "UPDATE beat b SET body = remap_uuids(b.body, @map::jsonb) FROM scene s WHERE s.id = b.scene_id AND s.book_id = @bookId",
            "UPDATE kb_progression k SET body = remap_uuids(k.body, @map::jsonb) FROM scene s WHERE s.id = k.scene_id AND s.book_id = @bookId",
            "UPDATE chapter SET summary = remap_uuids(summary, @map::jsonb), notes = remap_uuids(notes, @map::jsonb) WHERE book_id = @bookId",
            "UPDATE act SET summary = remap_uuids(summary, @map::jsonb) WHERE book_id = @bookId",
            "UPDATE kb_entry SET summary = remap_uuids(summary, @map::jsonb), body = remap_uuids(body, @map::jsonb) WHERE book_id = @bookId AND id <> @entryId",
        ];
        foreach (var sql in remaps)
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new { mapArgs.map, mapArgs.bookId, entryId }, transaction, cancellationToken: cancellationToken));
        }

        // The folded entry is archived with its links, not deleted (#85).
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE kb_entry SET archived_at = now() WHERE id = @entryId AND archived_at IS NULL",
            new { entryId }, transaction, cancellationToken: cancellationToken));
    }
}
