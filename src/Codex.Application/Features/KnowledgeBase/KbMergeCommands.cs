using System.Data.Common;
using System.Text.Json;
using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.KnowledgeBase;

public interface IKbMergeCommands
{
    Task<KbMergeImpactDto> ImpactAsync(Guid entryId, Guid intoId, CancellationToken cancellationToken);

    Task<KbEntryDto> MergeAsync(Guid entryId, Guid intoId, CancellationToken cancellationToken);

    // The same merge, also returning what it changed so UnmergeAsync can put it back.
    Task<KbMergeResult> MergeWithSnapshotAsync(Guid entryId, Guid intoId, CancellationToken cancellationToken);

    // Null when the merge can still be undone: the problem otherwise (something it touched has changed since).
    Task<string?> UnmergeProblemAsync(KbMergeSnapshot snapshot, CancellationToken cancellationToken);

    // Puts a merge back in one transaction: 409 when anything it touched has changed since, 404 when the merged entry is not archived.
    Task<KbEntryDto> UnmergeAsync(KbMergeSnapshot snapshot, CancellationToken cancellationToken);
}

// Merges a duplicate entry into another one, in one transaction (IProseStore.UpdateManyAsync with origin "kb_merge"):
// every use of the entry's id in a scene body (mentions, `::spell` / `::statblock` directives), summary, beat and other
// entry's text points at the target; scene links, beat context, timeline notes, related links and the references that
// name the entry move to the target; the target gains the entry's name and aliases as aliases; the entry's body, when
// it has one, is appended to the target's under "Merged from <name>"; the entry is archived (there is no Undo).
// The target keeps its own summary and body. Both ids are owned (404), live (404), different (400 validation), and the
// target must be in the entry's own book or in its series (400), so every scene that saw the entry still sees the target.
[RegisterSingleton]
public sealed class KbMergeCommands(NpgsqlDataSource dataSource, IProseStore proseStore, IKbEntryQueries queries, IBookEvents events, IBookAccess access)
    : IKbMergeCommands
{
    private const string MergeOrigin = "kb_merge";

    private sealed record EntryRow(Guid Id, Guid BookId, string Name, string Aliases, string Body);

    private sealed record Counts(int SceneLinks, int Additions, int Related);

    private sealed record Plan(EntryRow Source, EntryRow Target, List<string> NewAliases);

    public async Task<KbMergeImpactDto> ImpactAsync(Guid entryId, Guid intoId, CancellationToken cancellationToken)
    {
        await RequireBothAsync(entryId, intoId, BookRole.View, cancellationToken);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var plan = await PlanAsync(connection, entryId, intoId, cancellationToken);
        var counts = await connection.QuerySingleAsync<Counts>(new CommandDefinition(
            """
            SELECT (SELECT count(*) FROM scene_kb s WHERE s.kb_entry_id = @entryId AND s.archived_at IS NULL
                      AND NOT EXISTS (SELECT 1 FROM scene_kb t WHERE t.scene_id = s.scene_id AND t.kb_entry_id = @intoId
                                        AND t.archived_at IS NULL))::integer AS SceneLinks,
                   (SELECT count(*) FROM kb_progression WHERE kb_entry_id = @entryId AND archived_at IS NULL)::integer AS Additions,
                   (SELECT count(*) FROM kb_entry_related r JOIN kb_entry o ON o.id = r.related_id AND o.archived_at IS NULL
                      WHERE r.kb_entry_id = @entryId AND r.related_id <> @intoId
                        AND NOT EXISTS (SELECT 1 FROM kb_entry_related x WHERE x.kb_entry_id = @intoId AND x.related_id = r.related_id))::integer AS Related
            """,
            new { entryId, intoId }, cancellationToken: cancellationToken));
        return new KbMergeImpactDto(plan.Target.Name, counts.SceneLinks, counts.Additions, counts.Related, plan.NewAliases);
    }

    public async Task<KbEntryDto> MergeAsync(Guid entryId, Guid intoId, CancellationToken cancellationToken) =>
        (await MergeWithSnapshotAsync(entryId, intoId, cancellationToken)).Target;

    public async Task<KbMergeResult> MergeWithSnapshotAsync(Guid entryId, Guid intoId, CancellationToken cancellationToken)
    {
        await RequireBothAsync(entryId, intoId, BookRole.Write, cancellationToken);
        Plan plan;
        Guid[] sceneIds;
        var source = entryId.ToString();
        var target = intoId.ToString();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            plan = await PlanAsync(connection, entryId, intoId, cancellationToken);
            sceneIds = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT scene_id FROM prose WHERE strpos(body, @source) > 0", new { source }, cancellationToken: cancellationToken))).ToArray();
        }

        var befores = new Dictionary<Guid, string>();
        var edits = new List<(Guid sceneId, int expectedVersion, Func<string, string> edit)>();
        foreach (var sceneId in sceneIds)
        {
            var version = (await proseStore.GetAsync(sceneId, cancellationToken)).Version;
            edits.Add((sceneId, version, body =>
            {
                befores[sceneId] = body;
                return body.Replace(source, target, StringComparison.Ordinal);
            }));
        }
        KbMergeSnapshot? snapshot = null;
        var versions = await proseStore.UpdateManyAsync(edits, MergeOrigin, new ProseWriteOptions
        {
            InTransaction = async (connection, transaction, ct) => snapshot = await MoveRowsAsync(connection, transaction, entryId, intoId, plan, ct),
        }, cancellationToken);

        await PublishAsync(plan.Source.BookId, plan.Target.BookId);
        var scenes = sceneIds.Select((id, i) => new KbMergeSnapshot.SceneBody(id, versions[i], befores[id])).ToList();
        return new KbMergeResult(await queries.GetAsync(intoId, cancellationToken), snapshot! with { Scenes = scenes });
    }

    private async Task PublishAsync(Guid sourceBookId, Guid targetBookId)
    {
        await events.PublishAsync(new StructureChanged(targetBookId, "kb"));
        if (sourceBookId != targetBookId)
        {
            await events.PublishAsync(new StructureChanged(sourceBookId, "kb"));
        }
    }

    // Merging changes both entries (and rewrites the prose that mentions the source), so it needs write on both.
    private async Task RequireBothAsync(Guid entryId, Guid intoId, BookRole minimum, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, entryId, minimum, cancellationToken);
        await access.RequireAsync(BookItem.KbEntry, intoId, minimum, cancellationToken);
        if (entryId == intoId)
        {
            throw ApiException.Validation("An entry cannot be merged into itself.");
        }
    }

    private static async Task<Plan> PlanAsync(NpgsqlConnection connection, Guid entryId, Guid intoId, CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<EntryRow>(new CommandDefinition(
            """
            SELECT id, book_id AS BookId, name, to_json(aliases)::text AS Aliases, body
            FROM kb_entry WHERE id = ANY(@ids) AND archived_at IS NULL
            """,
            new { ids = new[] { entryId, intoId } }, cancellationToken: cancellationToken))).ToDictionary(r => r.Id);
        if (!rows.TryGetValue(entryId, out var source) || !rows.TryGetValue(intoId, out var target))
        {
            throw ApiException.NotFound("Knowledge base entry was not found.");
        }
        var seriesOfSource = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT series_id FROM book WHERE id = @bookId", new { bookId = source.BookId }, cancellationToken: cancellationToken));
        if (target.BookId != source.BookId && target.BookId != seriesOfSource)
        {
            throw ApiException.Validation("The entries must be in the same book, or the target in the source's series.");
        }

        var existing = (JsonSerializer.Deserialize<List<string>>(target.Aliases) ?? []).Append(target.Name).ToList();
        var added = (JsonSerializer.Deserialize<List<string>>(source.Aliases) ?? []).Prepend(source.Name)
            .Where(n => !existing.Contains(n, StringComparer.OrdinalIgnoreCase))
            .DistinctBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        return new Plan(source, target, added);
    }

    private static async Task<KbMergeSnapshot> MoveRowsAsync(
        DbConnection connection, DbTransaction transaction, Guid entryId, Guid intoId, Plan plan, CancellationToken cancellationToken)
    {
        async Task Run(string sql, object? parameters = null) =>
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));

        var arguments = new { entryId, intoId, source = entryId.ToString(), target = intoId.ToString() };
        var live = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE kb_entry SET archived_at = now() WHERE id = @entryId AND archived_at IS NULL", arguments, transaction, cancellationToken: cancellationToken));
        if (live == 0)
        {
            throw ApiException.NotFound($"Knowledge base entry {entryId} was not found.");
        }

        var snapshot = await CaptureAsync(connection, transaction, entryId, intoId, plan, arguments.source, cancellationToken);

        await Run("UPDATE scene SET summary = replace(summary, @source, @target) WHERE strpos(summary, @source) > 0", arguments);
        await Run("UPDATE beat SET body = replace(body, @source, @target) WHERE strpos(body, @source) > 0", arguments);
        await Run(
            """
            UPDATE kb_entry SET summary = replace(summary, @source, @target), body = replace(body, @source, @target)
            WHERE archived_at IS NULL AND id <> @intoId AND (strpos(body, @source) > 0 OR strpos(coalesce(summary, ''), @source) > 0)
            """, arguments);

        // The source's live links are archived; the target gains (or revives) the same links (#85).
        await Run(
            """
            INSERT INTO scene_kb (scene_id, kb_entry_id) SELECT scene_id, @intoId FROM scene_kb WHERE kb_entry_id = @entryId AND archived_at IS NULL
            ON CONFLICT (scene_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """, arguments);
        await Run("UPDATE scene_kb SET archived_at = now() WHERE kb_entry_id = @entryId AND archived_at IS NULL", arguments);
        await Run(
            """
            INSERT INTO beat_kb (beat_id, kb_entry_id) SELECT beat_id, @intoId FROM beat_kb WHERE kb_entry_id = @entryId AND archived_at IS NULL
            ON CONFLICT (beat_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """, arguments);
        await Run("UPDATE beat_kb SET archived_at = now() WHERE kb_entry_id = @entryId AND archived_at IS NULL", arguments);
        await Run("UPDATE kb_progression SET kb_entry_id = @intoId WHERE kb_entry_id = @entryId", arguments);
        await Run("UPDATE reference SET kb_entry_id = @intoId WHERE kb_entry_id = @entryId", arguments);
        await Run(
            """
            INSERT INTO kb_entry_link (entry_a_id, entry_b_id)
            SELECT least(o.other, @intoId), greatest(o.other, @intoId)
            FROM (SELECT CASE WHEN entry_a_id = @entryId THEN entry_b_id ELSE entry_a_id END AS other
                  FROM kb_entry_link WHERE (entry_a_id = @entryId OR entry_b_id = @entryId) AND archived_at IS NULL) o
            JOIN kb_entry e ON e.id = o.other AND e.archived_at IS NULL
            WHERE o.other <> @intoId
            ON CONFLICT (entry_a_id, entry_b_id) DO UPDATE SET archived_at = NULL
            """, arguments);
        await Run(
            "UPDATE kb_entry_link SET archived_at = now() WHERE (entry_a_id = @entryId OR entry_b_id = @entryId) AND archived_at IS NULL",
            arguments);

        var merged = MergedBody(plan, arguments.source, arguments.target);
        await Run(
            """
            UPDATE kb_entry SET aliases = aliases || @aliases::text[], body = @body, updated_at = now() WHERE id = @intoId
            """,
            new { intoId, aliases = plan.NewAliases.ToArray(), body = merged });
        return snapshot;
    }

    private static string MergedBody(Plan plan, string source, string target) =>
        plan.Source.Body.Trim().Length == 0
            ? plan.Target.Body
            : $"{plan.Target.Body.TrimEnd()}\n\n**Merged from {plan.Source.Name}**\n\n{plan.Source.Body.Trim().Replace(source, target, StringComparison.Ordinal)}";

    // What the move is about to change, read in the merge's transaction before any of it runs. The scene bodies are added by the caller.
    private static async Task<KbMergeSnapshot> CaptureAsync(
        DbConnection connection, DbTransaction transaction, Guid entryId, Guid intoId, Plan plan, string source, CancellationToken cancellationToken)
    {
        async Task<List<T>> Query<T>(string sql) => (await connection.QueryAsync<T>(new CommandDefinition(
            sql, new { entryId, intoId, source }, transaction, cancellationToken: cancellationToken))).ToList();

        var summaries = await Query<KbMergeSnapshot.Text>(
            "SELECT id, summary AS Value FROM scene WHERE strpos(summary, @source) > 0 ORDER BY id FOR UPDATE");
        var beats = await Query<KbMergeSnapshot.Text>(
            "SELECT id, body AS Value FROM beat WHERE strpos(body, @source) > 0 ORDER BY id FOR UPDATE");
        var entries = await Query<KbMergeSnapshot.EntryText>(
            """
            SELECT id, summary, body FROM kb_entry
            WHERE archived_at IS NULL AND id <> @intoId AND (strpos(body, @source) > 0 OR strpos(coalesce(summary, ''), @source) > 0)
            ORDER BY id FOR UPDATE
            """);
        var sceneLinks = await Query<Guid>("SELECT scene_id FROM scene_kb WHERE kb_entry_id = @entryId AND archived_at IS NULL ORDER BY scene_id");
        var sceneLinksAdded = await Query<Guid>(
            """
            SELECT s.scene_id FROM scene_kb s WHERE s.kb_entry_id = @entryId AND s.archived_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM scene_kb t WHERE t.scene_id = s.scene_id AND t.kb_entry_id = @intoId AND t.archived_at IS NULL)
            ORDER BY s.scene_id
            """);
        var beatContexts = await Query<Guid>("SELECT beat_id FROM beat_kb WHERE kb_entry_id = @entryId AND archived_at IS NULL ORDER BY beat_id");
        var beatContextsAdded = await Query<Guid>(
            """
            SELECT s.beat_id FROM beat_kb s WHERE s.kb_entry_id = @entryId AND s.archived_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM beat_kb t WHERE t.beat_id = s.beat_id AND t.kb_entry_id = @intoId AND t.archived_at IS NULL)
            ORDER BY s.beat_id
            """);
        var additions = await Query<Guid>("SELECT id FROM kb_progression WHERE kb_entry_id = @entryId ORDER BY id");
        var references = await Query<Guid>("SELECT id FROM reference WHERE kb_entry_id = @entryId ORDER BY id");
        var related = await Query<Guid>("SELECT related_id FROM kb_entry_related WHERE kb_entry_id = @entryId ORDER BY related_id");
        var relatedAdded = await Query<Guid>(
            """
            SELECT r.related_id FROM kb_entry_related r JOIN kb_entry o ON o.id = r.related_id AND o.archived_at IS NULL
            WHERE r.kb_entry_id = @entryId AND r.related_id <> @intoId
              AND NOT EXISTS (SELECT 1 FROM kb_entry_related x WHERE x.kb_entry_id = @intoId AND x.related_id = r.related_id)
            ORDER BY r.related_id
            """);

        var aliasesBefore = JsonSerializer.Deserialize<List<string>>(plan.Target.Aliases) ?? [];
        return new KbMergeSnapshot(
            entryId, intoId, [], summaries, beats, entries, sceneLinks, sceneLinksAdded, beatContexts, beatContextsAdded,
            additions, references, related, relatedAdded, aliasesBefore, plan.Target.Body,
            aliasesBefore.Concat(plan.NewAliases).ToList(), MergedBody(plan, source, intoId.ToString()));
    }

    public async Task<string?> UnmergeProblemAsync(KbMergeSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        foreach (var scene in snapshot.Scenes)
        {
            if ((await proseStore.GetAsync(scene.SceneId, cancellationToken)).Version != scene.Version)
            {
                return "A scene this merge changed was edited since.";
            }
        }
        return await ProblemAsync(connection, null, snapshot, cancellationToken);
    }

    public async Task<KbEntryDto> UnmergeAsync(KbMergeSnapshot snapshot, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.KbEntry, snapshot.SourceId, BookRole.Write, cancellationToken);
        await access.RequireAsync(BookItem.KbEntry, snapshot.TargetId, BookRole.Write, cancellationToken);
        Guid sourceBookId;
        Guid targetBookId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            var books = (await connection.QueryAsync<(Guid Id, Guid BookId, bool Archived)>(new CommandDefinition(
                "SELECT id, book_id, archived_at IS NOT NULL FROM kb_entry WHERE id = ANY(@ids)",
                new { ids = new[] { snapshot.SourceId, snapshot.TargetId } }, cancellationToken: cancellationToken))).ToDictionary(r => r.Id);
            if (!books.TryGetValue(snapshot.SourceId, out var sourceRow) || !sourceRow.Archived || !books.TryGetValue(snapshot.TargetId, out var targetRow))
            {
                throw ApiException.NotFound($"Merged knowledge base entry {snapshot.SourceId} was not found.");
            }
            (sourceBookId, targetBookId) = (sourceRow.BookId, targetRow.BookId);
        }

        // Scene versions are checked by the prose store inside the transaction (a moved-on scene is 409); the rest by ProblemAsync after the lock.
        await proseStore.UpdateManyAsync(
            snapshot.Scenes.Select(s => (s.SceneId, s.Version, (Func<string, string>)(_ => s.Body))).ToList(), "restore",
            new ProseWriteOptions { InTransaction = (connection, transaction, ct) => UnmoveRowsAsync(connection, transaction, snapshot, ct) },
            cancellationToken);
        await PublishAsync(sourceBookId, targetBookId);
        return await queries.GetAsync(snapshot.SourceId, cancellationToken);
    }

    // The rows a merge moved, as the merge left them: texts replaced, links and notes on the target, the target's aliases and body.
    private static async Task<string?> ProblemAsync(DbConnection c, DbTransaction? t, KbMergeSnapshot snapshot, CancellationToken ct)
    {
        var (source, target) = (snapshot.SourceId.ToString(), snapshot.TargetId.ToString());
        string Moved(string text) => text.Replace(source, target, StringComparison.Ordinal);
        Task<T?> One<T>(string sql, object args) => c.ExecuteScalarAsync<T>(new CommandDefinition(sql, args, t, cancellationToken: ct));

        var entries = (await c.QueryAsync<(Guid Id, bool Archived, string Aliases, string Body)>(new CommandDefinition(
            "SELECT id, archived_at IS NOT NULL, to_json(aliases)::text, body FROM kb_entry WHERE id = ANY(@ids)",
            new { ids = new[] { snapshot.SourceId, snapshot.TargetId } }, t, cancellationToken: ct))).ToDictionary(e => e.Id);
        if (!entries.TryGetValue(snapshot.SourceId, out var duplicate) || !duplicate.Archived
            || !entries.TryGetValue(snapshot.TargetId, out var kept) || kept.Archived)
        {
            return "An entry this merge touched was deleted, restored or merged again.";
        }
        if (await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM kb_entry e JOIN kb_category k ON k.id = e.category_id WHERE e.id = @id AND k.archived_at IS NOT NULL)",
                new { id = snapshot.SourceId }, t, cancellationToken: ct)))
        {
            return "The category of the merged entry was deleted.";
        }
        var aliases = JsonSerializer.Deserialize<List<string>>(kept.Aliases) ?? [];
        if (kept.Body != snapshot.BodyAfter || !aliases.SequenceEqual(snapshot.AliasesAfter))
        {
            return "The entry this was merged into was edited since.";
        }

        var changed = false;
        foreach (var s in snapshot.Summaries)
        {
            changed |= await One<string?>("SELECT summary FROM scene WHERE id = @id", new { id = s.Id }) != Moved(s.Value);
        }
        foreach (var b in snapshot.Beats)
        {
            changed |= await One<string?>("SELECT body FROM beat WHERE id = @id", new { id = b.Id }) != Moved(b.Value);
        }
        foreach (var e in snapshot.Entries)
        {
            var now = await c.QuerySingleOrDefaultAsync<(string? Summary, string Body)?>(new CommandDefinition(
                "SELECT summary, body FROM kb_entry WHERE id = @id", new { id = e.Id }, t, cancellationToken: ct));
            changed |= now is null || now.Value.Body != Moved(e.Body) || now.Value.Summary != (e.Summary == null ? null : Moved(e.Summary));
        }
        if (changed)
        {
            return "A summary, beat or entry text this merge changed was edited since.";
        }

        var args = new
        {
            sourceId = snapshot.SourceId, targetId = snapshot.TargetId, scenes = snapshot.SceneLinksAdded.ToArray(), beats = snapshot.BeatContextsAdded.ToArray(),
            additions = snapshot.Additions.ToArray(), references = snapshot.References.ToArray(), related = snapshot.RelatedAdded.ToArray(),
        };
        var intact = await One<bool>(
            """
            SELECT (SELECT count(*) FROM scene_kb WHERE kb_entry_id = @targetId AND scene_id = ANY(@scenes) AND archived_at IS NULL) = cardinality(@scenes)
               AND (SELECT count(*) FROM beat_kb WHERE kb_entry_id = @targetId AND beat_id = ANY(@beats) AND archived_at IS NULL) = cardinality(@beats)
               AND (SELECT count(*) FROM kb_progression WHERE kb_entry_id = @targetId AND id = ANY(@additions)) = cardinality(@additions)
               AND (SELECT count(*) FROM reference WHERE kb_entry_id = @targetId AND id = ANY(@references)) = cardinality(@references)
               AND (SELECT count(*) FROM kb_entry_related WHERE kb_entry_id = @targetId AND related_id = ANY(@related)) = cardinality(@related)
            """, args);
        return intact ? null : "A link, timeline note or reference this merge moved was changed since.";
    }

    private static async Task UnmoveRowsAsync(
        DbConnection connection, DbTransaction transaction, KbMergeSnapshot snapshot, CancellationToken cancellationToken)
    {
        async Task Run(string sql, object? parameters = null) =>
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));

        await Run("SELECT 1 FROM kb_entry WHERE id = ANY(@ids) ORDER BY id FOR UPDATE", new { ids = new[] { snapshot.SourceId, snapshot.TargetId } });
        if (await ProblemAsync(connection, transaction, snapshot, cancellationToken) is { } problem)
        {
            throw ApiException.Conflict($"{problem} The merge cannot be undone.");
        }

        var arguments = new
        {
            sourceId = snapshot.SourceId, targetId = snapshot.TargetId,
            sceneLinks = snapshot.SceneLinks.ToArray(), sceneLinksAdded = snapshot.SceneLinksAdded.ToArray(),
            beatContexts = snapshot.BeatContexts.ToArray(), beatContextsAdded = snapshot.BeatContextsAdded.ToArray(),
            additions = snapshot.Additions.ToArray(), references = snapshot.References.ToArray(),
            related = snapshot.Related.ToArray(), relatedAdded = snapshot.RelatedAdded.ToArray(),
            aliases = snapshot.AliasesBefore.ToArray(), body = snapshot.BodyBefore,
        };
        await Run("UPDATE kb_entry SET archived_at = NULL WHERE id = @sourceId", arguments);
        foreach (var s in snapshot.Summaries)
        {
            await Run("UPDATE scene SET summary = @value WHERE id = @id", new { id = s.Id, value = s.Value });
        }
        foreach (var b in snapshot.Beats)
        {
            await Run("UPDATE beat SET body = @value WHERE id = @id", new { id = b.Id, value = b.Value });
        }
        foreach (var e in snapshot.Entries)
        {
            await Run("UPDATE kb_entry SET summary = @summary, body = @body WHERE id = @id", new { id = e.Id, summary = e.Summary, body = e.Body });
        }

        // The links the merge added to the target are archived again, the source's archived ones revived (#85).
        await Run(
            "UPDATE scene_kb SET archived_at = now() WHERE kb_entry_id = @targetId AND scene_id = ANY(@sceneLinksAdded) AND archived_at IS NULL",
            arguments);
        await Run(
            """
            INSERT INTO scene_kb (scene_id, kb_entry_id) SELECT s.id, @sourceId FROM scene s WHERE s.id = ANY(@sceneLinks)
            ON CONFLICT (scene_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """, arguments);
        await Run(
            "UPDATE beat_kb SET archived_at = now() WHERE kb_entry_id = @targetId AND beat_id = ANY(@beatContextsAdded) AND archived_at IS NULL",
            arguments);
        await Run(
            """
            INSERT INTO beat_kb (beat_id, kb_entry_id) SELECT b.id, @sourceId FROM beat b WHERE b.id = ANY(@beatContexts)
            ON CONFLICT (beat_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """, arguments);
        await Run("UPDATE kb_progression SET kb_entry_id = @sourceId WHERE id = ANY(@additions)", arguments);
        await Run("UPDATE reference SET kb_entry_id = @sourceId WHERE id = ANY(@references)", arguments);
        await Run(
            """
            UPDATE kb_entry_link SET archived_at = now()
            WHERE ((entry_a_id = @targetId AND entry_b_id = ANY(@relatedAdded)) OR (entry_b_id = @targetId AND entry_a_id = ANY(@relatedAdded)))
              AND archived_at IS NULL
            """, arguments);
        await Run(
            """
            INSERT INTO kb_entry_link (entry_a_id, entry_b_id)
            SELECT least(@sourceId, o.id), greatest(@sourceId, o.id) FROM kb_entry o WHERE o.id = ANY(@related)
            ON CONFLICT (entry_a_id, entry_b_id) DO UPDATE SET archived_at = NULL
            """, arguments);
        await Run("UPDATE kb_entry SET aliases = @aliases::text[], body = @body WHERE id = @targetId", arguments);
    }
}
