using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Events;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Structure;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

// Merge a scene into the one before it in its chapter. The earlier scene keeps its place and takes the later scene's
// prose, summary, labels, Knowledge base links and everything its text points at (beats, Codex additions, figures, source
// uses), all in one transaction with the prose write. The later scene is archived, not deleted, with an empty body; the
// text each scene held before is kept as a revision, so version history can bring it back.
[RegisterSingleton]
public sealed class SceneMergeCommands(NpgsqlDataSource dataSource, IBookAccess access, IProseStore prose, IBookEvents events)
{
    private sealed record Pair(Guid BookId, Guid PreviousId);

    private sealed record SceneRow(Guid BookId, Guid ChapterId, string Position);

    public async Task<MergeSceneResult> MergeIntoPreviousAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, notFound: "Scene not found.");
        Pair pair;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            var found = await connection.QuerySingleOrDefaultAsync<SceneRow>(new CommandDefinition(
                """
                SELECT s.book_id AS BookId, s.chapter_id AS ChapterId, s.position
                FROM scene s JOIN chapter c ON c.id = s.chapter_id JOIN act a ON a.id = c.act_id
                WHERE s.id = @sceneId AND s.archived_at IS NULL AND c.archived_at IS NULL AND a.archived_at IS NULL
                """,
                new { sceneId }, cancellationToken: cancellationToken))
                ?? throw ApiException.NotFound("Scene not found.");
            var previous = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                """
                SELECT id FROM scene WHERE chapter_id = @ChapterId AND archived_at IS NULL AND position < @Position
                ORDER BY position DESC, id DESC LIMIT 1
                """,
                new { found.ChapterId, found.Position }, cancellationToken: cancellationToken))
                ?? throw ApiException.Validation("There is no earlier scene in this chapter to merge into.");
            pair = new Pair(found.BookId, previous);
        }

        var earlier = await prose.GetAsync(pair.PreviousId, cancellationToken);
        var later = await prose.GetAsync(sceneId, cancellationToken);
        var merged = Join(earlier.Body, later.Body);

        var versions = await prose.UpdateManyAsync(
            [
                (pair.PreviousId, earlier.Version, _ => merged),
                (sceneId, later.Version, _ => ""),
            ],
            "manual",
            new ProseWriteOptions
            {
                BeforeWrites = (connection, transaction, ct) => MoveContentAsync(connection, transaction, pair.PreviousId, sceneId, ct),
                InTransaction = async (connection, transaction, ct) =>
                    await connection.ExecuteAsync(new CommandDefinition(
                        "UPDATE scene SET archived_at = now(), updated_at = now() WHERE id = @sceneId",
                        new { sceneId }, transaction, cancellationToken: ct)),
            },
            cancellationToken);

        await events.PublishAsync(new StructureChanged(pair.BookId, "scene"));
        return new MergeSceneResult(pair.PreviousId, versions[0], sceneId);
    }

    // Blocks are separated by exactly one blank line and a non-empty body ends in one newline (docs/prose-format.md).
    private static string Join(string earlier, string later)
    {
        if (earlier.Length == 0 || later.Length == 0)
        {
            return earlier + later;
        }
        return earlier.TrimEnd('\n') + "\n\n" + later;
    }

    private static async Task MoveContentAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, Guid earlier, Guid later, CancellationToken ct)
    {
        var pending = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM ai_generation WHERE scene_id IN (@earlier, @later) AND status IN ('streaming', 'pending'))",
            new { earlier, later }, transaction, cancellationToken: ct));
        if (pending)
        {
            throw ApiException.Blocked("Apply or discard the draft first.");
        }

        var args = new { earlier, later };
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO prose_revision (scene_id, body, source)
            SELECT scene_id, body, 'manual' FROM prose WHERE scene_id IN (@earlier, @later) AND body <> ''
            """,
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE scene e SET updated_at = now(),
                summary = CASE
                    WHEN coalesce(e.summary, '') = '' THEN l.summary
                    WHEN coalesce(l.summary, '') = '' THEN e.summary
                    ELSE e.summary || E'\n\n' || l.summary END
            FROM scene l WHERE e.id = @earlier AND l.id = @later
            """,
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO scene_label (scene_id, label_id) SELECT @earlier, sl.label_id FROM scene_label sl JOIN label l ON l.id = sl.label_id AND l.archived_at IS NULL WHERE sl.scene_id = @later AND sl.archived_at IS NULL ON CONFLICT (scene_id, label_id) DO UPDATE SET archived_at = NULL",
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scene_kb (scene_id, kb_entry_id) SELECT @earlier, kb_entry_id FROM scene_kb WHERE scene_id = @later AND archived_at IS NULL
            ON CONFLICT (scene_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """,
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE beat SET scene_id = @earlier, updated_at = now() WHERE scene_id = @later", args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE kb_progression SET scene_id = @earlier, updated_at = now() WHERE scene_id = @later", args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE figure SET scene_id = @earlier, updated_at = now() WHERE scene_id = @later", args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference_use SET scene_id = @earlier, updated_at = now() WHERE scene_id = @later", args, transaction, cancellationToken: ct));
    }
}
