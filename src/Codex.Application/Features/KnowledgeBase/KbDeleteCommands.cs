using System.Data.Common;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Application.Prose;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbDeleteCommands
{
    Task<KbDeleteImpactDto> ImpactAsync(Guid entryId, CancellationToken cancellationToken);

    Task<KbUndoRecordDto> DeleteAsync(Guid entryId, CancellationToken cancellationToken);

    Task<KbEntryDto> RestoreAsync(Guid entryId, KbUndoRecordDto record, CancellationToken cancellationToken);
}

// Delete is a soft delete (archived_at), all in one transaction: every [Name](kb:id) in a scene body becomes Name and
// the entry's ::kb-addition lines are stripped (IProseStore.UpdateManyAsync, origin "kb_delete"; the schema archives
// the notes), then, in the same transaction (ProseWriteOptions.InTransaction), the same mentions in scene summaries,
// beat bodies and other entries' text become plain text, the live related, scene and beat links are archived and the
// entry is archived. Any failure leaves everything as it was. The returned record holds what was changed (the links it
// archived, by id); the server keeps nothing. Restore re-applies it, reviving those link rows, again in one
// transaction, after checking that the record belongs to this entry's book and that nothing changed since (409).
// Unknown or already deleted entry 404 (restore: an entry that is not deleted).
[RegisterSingleton]
public sealed class KbDeleteCommands(NpgsqlDataSource dataSource, IProseStore proseStore, IKbEntryQueries queries, IBookEvents events, IBookAccess access)
    : IKbDeleteCommands
{
    private const string DeleteOrigin = "kb_delete";

    private sealed record SceneText(Guid Id, string? Text);

    private sealed record BeatText(Guid Id, string Text);

    private sealed record EntryText(Guid Id, string? Summary, string Body);

    private sealed record LiveRow(Guid BookId, string Name);

    private sealed record AdditionRow(Guid Id, Guid SceneId);

    private static string Needle(Guid entryId) => $"(kb:{entryId})";

    private static string Unmention(string text, Guid entryId) => ProseDocument.Parse(text).ReplaceMentionWithText(entryId).ToString();

    public async Task<KbDeleteImpactDto> ImpactAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await RequireLiveAsync(connection, entryId, cancellationToken);
        var needle = Needle(entryId);
        return await connection.QuerySingleAsync<KbDeleteImpactDto>(new CommandDefinition(
            KbEntryUseSql.With + """

            SELECT
              ((SELECT coalesce(sum((length(body) - length(replace(body, @needle, ''))) / length(@needle)), 0) FROM prose)
             + (SELECT coalesce(sum((length(summary) - length(replace(summary, @needle, ''))) / length(@needle)), 0) FROM scene WHERE summary IS NOT NULL)
             + (SELECT coalesce(sum((length(body) - length(replace(body, @needle, ''))) / length(@needle)), 0) FROM beat WHERE archived_at IS NULL)
             + (SELECT coalesce(sum((length(body) - length(replace(body, @needle, ''))) / length(@needle)), 0) FROM kb_entry WHERE archived_at IS NULL AND id <> @entryId))::integer AS Mentions,
              (SELECT count(DISTINCT scene_id) FROM entry_use)::integer AS Scenes,
              (SELECT count(*) FROM kb_progression WHERE kb_entry_id = @entryId AND archived_at IS NULL)::integer AS Notes,
              (SELECT count(*) FROM kb_entry_related r JOIN kb_entry o ON o.id = r.related_id AND o.archived_at IS NULL
                WHERE r.kb_entry_id = @entryId)::integer AS Links
            """,
            new { entryId, needle, idText = entryId.ToString() }, cancellationToken: cancellationToken));
    }

    public async Task<KbUndoRecordDto> DeleteAsync(Guid entryId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.Write, cancellationToken);
        string name;
        Guid ownerId;
        Guid[] sceneIds;
        ILookup<Guid, Guid> additions;
        var needle = Needle(entryId);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            (ownerId, name) = await RequireLiveAsync(connection, entryId, cancellationToken);
            additions = (await connection.QueryAsync<AdditionRow>(new CommandDefinition(
                "SELECT id, scene_id AS SceneId FROM kb_progression WHERE kb_entry_id = @entryId AND archived_at IS NULL",
                new { entryId }, cancellationToken: cancellationToken))).ToLookup(a => a.SceneId, a => a.Id);
            sceneIds = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT scene_id FROM prose WHERE strpos(body, @needle) > 0
                UNION SELECT scene_id FROM kb_progression WHERE kb_entry_id = @entryId AND archived_at IS NULL
                """,
                new { entryId, needle }, cancellationToken: cancellationToken))).ToArray();
        }

        KbUndoRecordDto? archived = null;
        var scenes = await UnmentionScenesAsync(sceneIds, entryId, additions, new ProseWriteOptions
        {
            InTransaction = async (connection, transaction, ct) => archived = await ArchiveAsync(connection, transaction, entryId, name, needle, ct),
        }, cancellationToken);
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return archived! with { Scenes = scenes };
    }

    // All scenes in one IProseStore transaction, with `options.InTransaction` after them: a stale version, a failing
    // embed check or a failing archive step leaves every scene as it was.
    private async Task<List<KbUndoSceneDto>> UnmentionScenesAsync(
        Guid[] sceneIds, Guid entryId, ILookup<Guid, Guid> additions, ProseWriteOptions options, CancellationToken cancellationToken)
    {
        var befores = new Dictionary<Guid, string>();
        var edits = new List<(Guid sceneId, int expectedVersion, Func<string, string> edit)>();
        foreach (var sceneId in sceneIds)
        {
            var version = (await proseStore.GetAsync(sceneId, cancellationToken)).Version;
            var additionIds = additions[sceneId].ToList();
            edits.Add((sceneId, version, body =>
            {
                befores[sceneId] = body;
                return WithoutEntry(body, entryId, additionIds);
            }));
        }
        var versions = await proseStore.UpdateManyAsync(edits, DeleteOrigin, options, cancellationToken);
        return sceneIds.Select((id, i) => new KbUndoSceneDto(id, versions[i], befores[id])).ToList();
    }

    private static string WithoutEntry(string body, Guid entryId, IEnumerable<Guid> additionIds)
    {
        var document = ProseDocument.Parse(body);
        var present = document.ListEmbeds().Where(e => e.Kind == ProseEmbedKind.KbAddition).Select(e => e.Id).ToHashSet();
        foreach (var id in additionIds.Where(present.Contains))
        {
            document = document.StripKbAddition(id);
        }
        return document.ReplaceMentionWithText(entryId).ToString();
    }

    // Everything outside the scene bodies, in the prose write's transaction: the entry, texts, links and notes. The
    // record it returns has no scenes yet (the caller adds them with their new versions).
    private static async Task<KbUndoRecordDto> ArchiveAsync(
        DbConnection connection, DbTransaction transaction, Guid entryId, string name, string needle, CancellationToken cancellationToken)
    {
        var live = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE kb_entry SET archived_at = now() WHERE id = @entryId AND archived_at IS NULL",
            new { entryId }, transaction, cancellationToken: cancellationToken));
        if (live == 0)
        {
            throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
        }

        var summaries = (await connection.QueryAsync<SceneText>(new CommandDefinition(
            "SELECT id, summary AS Text FROM scene WHERE strpos(summary, @needle) > 0 ORDER BY id FOR UPDATE",
            new { needle }, transaction, cancellationToken: cancellationToken))).ToList();
        foreach (var s in summaries)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scene SET summary = @text WHERE id = @id",
                new { id = s.Id, text = Unmention(s.Text!, entryId) }, transaction, cancellationToken: cancellationToken));
        }

        var beats = (await connection.QueryAsync<BeatText>(new CommandDefinition(
            "SELECT id, body AS Text FROM beat WHERE archived_at IS NULL AND strpos(body, @needle) > 0 ORDER BY id FOR UPDATE",
            new { needle }, transaction, cancellationToken: cancellationToken))).ToList();
        foreach (var b in beats)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE beat SET body = @text WHERE id = @id",
                new { id = b.Id, text = Unmention(b.Text, entryId) }, transaction, cancellationToken: cancellationToken));
        }

        var entries = (await connection.QueryAsync<EntryText>(new CommandDefinition(
            """
            SELECT id, summary, body FROM kb_entry
            WHERE archived_at IS NULL AND id <> @entryId AND (strpos(body, @needle) > 0 OR strpos(coalesce(summary, ''), @needle) > 0)
            ORDER BY id FOR UPDATE
            """,
            new { entryId, needle }, transaction, cancellationToken: cancellationToken))).ToList();
        foreach (var e in entries)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE kb_entry SET summary = @summary, body = @body WHERE id = @id",
                new
                {
                    id = e.Id,
                    summary = e.Summary == null ? null : Unmention(e.Summary, entryId),
                    body = Unmention(e.Body, entryId),
                }, transaction, cancellationToken: cancellationToken));
        }

        var related = (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            UPDATE kb_entry_link SET archived_at = now()
            WHERE (entry_a_id = @entryId OR entry_b_id = @entryId) AND archived_at IS NULL
            RETURNING CASE WHEN entry_a_id = @entryId THEN entry_b_id ELSE entry_a_id END
            """,
            new { entryId }, transaction, cancellationToken: cancellationToken))).ToArray();
        var sceneLinks = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "UPDATE scene_kb SET archived_at = now() WHERE kb_entry_id = @entryId AND archived_at IS NULL RETURNING scene_id",
            new { entryId }, transaction, cancellationToken: cancellationToken))).ToArray();
        var beatContexts = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "UPDATE beat_kb SET archived_at = now() WHERE kb_entry_id = @entryId AND archived_at IS NULL RETURNING beat_id",
            new { entryId }, transaction, cancellationToken: cancellationToken))).ToArray();
        // Notes whose line was not in the saved prose (the lines of the rest were stripped above).
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE kb_progression SET archived_at = now() WHERE kb_entry_id = @entryId AND archived_at IS NULL",
            new { entryId }, transaction, cancellationToken: cancellationToken));

        return new KbUndoRecordDto(
            entryId, name, [],
            summaries.Select(s => new KbUndoSummaryDto(s.Id, s.Text!)).ToList(),
            beats.Select(b => new KbUndoBeatDto(b.Id, b.Text)).ToList(),
            entries.Select(e => new KbUndoEntryTextDto(e.Id, e.Summary, e.Body)).ToList(),
            related, sceneLinks, beatContexts);
    }

    public async Task<KbEntryDto> RestoreAsync(Guid entryId, KbUndoRecordDto record, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, BookRole.Write, cancellationToken);
        if (record.EntryId != entryId)
        {
            throw ApiException.Validation("The undo record is for another entry.");
        }
        Guid ownerId;
        Guid[] noteIds;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            ownerId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT book_id FROM kb_entry WHERE id = @entryId AND archived_at IS NOT NULL", new { entryId }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound($"Deleted knowledge base entry {entryId} was not found.");
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT EXISTS (SELECT 1 FROM kb_entry e JOIN kb_category k ON k.id = e.category_id WHERE e.id = @entryId AND k.archived_at IS NOT NULL)",
                    new { entryId }, cancellationToken: cancellationToken)))
            {
                throw ApiException.Conflict("The entry's category was deleted, so the delete cannot be undone until the category is back.");
            }
            await RequireInBookAsync(connection, ownerId, record, cancellationToken);
            await RequireUnchangedAsync(connection, entryId, record, cancellationToken);
            noteIds = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT id FROM kb_progression WHERE kb_entry_id = @entryId", new { entryId }, cancellationToken: cancellationToken))).ToArray();
        }
        foreach (var scene in record.Scenes)
        {
            var prose = await proseStore.GetAsync(scene.SceneId, cancellationToken);
            if (prose.Version != scene.Version || prose.Body != WithoutEntry(scene.Body, entryId, noteIds))
            {
                throw ApiException.Conflict("A scene changed after the entry was deleted, so the delete cannot be undone.");
            }
        }

        // The saved bodies bring the notes' lines back (the schema revives the notes with them); the entry, its texts and
        // links come back in the same transaction.
        await proseStore.UpdateManyAsync(
            record.Scenes.Select(s => (s.SceneId, s.Version, (Func<string, string>)(_ => s.Body))).ToList(), "restore",
            new ProseWriteOptions { InTransaction = (connection, transaction, ct) => UnarchiveAsync(connection, transaction, entryId, record, ct) },
            cancellationToken);
        await events.PublishAsync(new StructureChanged(ownerId, "kb"));
        return await queries.GetAsync(entryId, cancellationToken);
    }

    private static async Task UnarchiveAsync(
        DbConnection connection, DbTransaction transaction, Guid entryId, KbUndoRecordDto record, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE kb_entry SET archived_at = NULL WHERE id = @entryId", new { entryId }, transaction, cancellationToken: cancellationToken));
        foreach (var s in record.Summaries)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE scene SET summary = @text WHERE id = @id", new { id = s.SceneId, text = s.Summary }, transaction, cancellationToken: cancellationToken));
        }
        foreach (var b in record.Beats)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE beat SET body = @text WHERE id = @id", new { id = b.BeatId, text = b.Body }, transaction, cancellationToken: cancellationToken));
        }
        foreach (var e in record.Entries)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE kb_entry SET summary = @summary, body = @body WHERE id = @id",
                new { id = e.EntryId, summary = e.Summary, body = e.Body }, transaction, cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO kb_entry_link (entry_a_id, entry_b_id)
            SELECT least(@entryId, o.id), greatest(@entryId, o.id) FROM kb_entry o
            WHERE o.id = ANY(@related) AND o.archived_at IS NULL
            ON CONFLICT (entry_a_id, entry_b_id) DO UPDATE SET archived_at = NULL
            """,
            new { entryId, related = record.Related.ToArray() }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scene_kb (scene_id, kb_entry_id) SELECT s.id, @entryId FROM scene s WHERE s.id = ANY(@ids)
            ON CONFLICT (scene_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """,
            new { entryId, ids = record.SceneLinks.ToArray() }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO beat_kb (beat_id, kb_entry_id) SELECT b.id, @entryId FROM beat b WHERE b.id = ANY(@ids)
            ON CONFLICT (beat_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """,
            new { entryId, ids = record.BeatContexts.ToArray() }, transaction, cancellationToken: cancellationToken));
    }

    // The record is client-supplied (no authentication in v1): it may only name this book's scenes, beats and entries.
    private static async Task RequireInBookAsync(
        NpgsqlConnection connection, Guid bookId, KbUndoRecordDto record, CancellationToken cancellationToken)
    {
        var scenes = record.Scenes.Select(s => s.SceneId).Concat(record.Summaries.Select(s => s.SceneId)).Concat(record.SceneLinks).Distinct().ToArray();
        var beats = record.Beats.Select(b => b.BeatId).Concat(record.BeatContexts).Distinct().ToArray();
        var entries = record.Entries.Select(e => e.EntryId).Concat(record.Related).Distinct().ToArray();
        var inBook = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT (SELECT count(*) FROM scene WHERE id = ANY(@scenes) AND book_id = @bookId)
                 + (SELECT count(*) FROM beat b JOIN scene s ON s.id = b.scene_id WHERE b.id = ANY(@beats) AND s.book_id = @bookId)
                 + (SELECT count(*) FROM kb_entry WHERE id = ANY(@entries) AND book_id = @bookId)
            """,
            new { bookId, scenes, beats, entries }, cancellationToken: cancellationToken));
        if (inBook != scenes.Length + beats.Length + entries.Length)
        {
            throw ApiException.Validation("The undo record names something outside this entry's book.");
        }
    }

    // Summaries, beats and entry texts must still be what the delete left (the record's text with mentions removed).
    private static async Task RequireUnchangedAsync(
        NpgsqlConnection connection, Guid entryId, KbUndoRecordDto record, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var s in record.Summaries)
        {
            var now = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT summary FROM scene WHERE id = @id", new { id = s.SceneId }, cancellationToken: cancellationToken));
            changed |= now != Unmention(s.Summary, entryId);
        }
        foreach (var b in record.Beats)
        {
            var now = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT body FROM beat WHERE id = @id", new { id = b.BeatId }, cancellationToken: cancellationToken));
            changed |= now != Unmention(b.Body, entryId);
        }
        foreach (var e in record.Entries)
        {
            var now = await connection.QuerySingleOrDefaultAsync<EntryText>(new CommandDefinition(
                "SELECT id, summary, body FROM kb_entry WHERE id = @id", new { id = e.EntryId }, cancellationToken: cancellationToken));
            changed |= now is null || now.Body != Unmention(e.Body, entryId)
                || now.Summary != (e.Summary == null ? null : Unmention(e.Summary, entryId));
        }
        if (changed)
        {
            throw ApiException.Conflict("Something changed after the entry was deleted, so the delete cannot be undone.");
        }
    }

    private static async Task<(Guid OwnerId, string Name)> RequireLiveAsync(
        NpgsqlConnection connection, Guid entryId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<LiveRow>(new CommandDefinition(
            "SELECT book_id AS BookId, name FROM kb_entry WHERE id = @entryId AND archived_at IS NULL",
            new { entryId }, cancellationToken: cancellationToken));
        return row != null ? (row.BookId, row.Name) : throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
    }
}
