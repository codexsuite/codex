using System.Data.Common;
using Agoda.IoC.Core;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Core;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Manuscript;

// Beats, scene splits and block moves. Positions never reach the server: the client sends the whole new bodies, and
// the beat rows follow the ::beat lines found in them. Every multi-scene step runs in the one transaction of
// ProseStore.UpdateManyAsync, so a stale version or a failing trigger leaves every scene and beat as it was.
[RegisterSingleton]
public sealed class BlockCommands(NpgsqlDataSource dataSource, IProseStore prose, IBookAccess access)
{
    private const string EditorOrigin = "editor";

    private sealed record SceneRow(Guid BookId, Guid ChapterId, string Position);

    public async Task<BookBeatsDto> ListAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, "Book not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<BeatRow>(new CommandDefinition(
            """
            SELECT b.id AS Id, b.scene_id AS SceneId, b.body AS Body, b.target_word_count AS TargetWordCount, b.is_hidden AS IsHidden, b.title AS Title,
                   coalesce((SELECT to_json(array_agg(k.kb_entry_id ORDER BY k.kb_entry_id))::text FROM beat_kb k WHERE k.beat_id = b.id AND k.archived_at IS NULL), '[]') AS Context
            FROM beat b JOIN scene s ON s.id = b.scene_id
            WHERE s.book_id = @bookId AND b.archived_at IS NULL AND s.archived_at IS NULL
            ORDER BY b.created_at, b.id
            """,
            new { bookId }, cancellationToken: cancellationToken));
        return new BookBeatsDto(bookId, rows.Select(r => r.ToDto()).ToList());
    }

    private sealed record BeatRow(Guid Id, Guid SceneId, string Body, int? TargetWordCount, bool IsHidden, string Title, string Context)
    {
        public BeatDto ToDto() => new(Id, SceneId, Body, TargetWordCount, IsHidden,
            System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(Context) ?? [], Title);
    }

    public async Task<InsertBeatResult> InsertBeatAsync(Guid sceneId, InsertBeatRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, "Scene not found.");
        var body = request.Body ?? throw ApiException.Validation("Prose body is required.");
        if (!BeatIds(body).Contains(request.BeatId))
        {
            throw ApiException.Validation("The body has no ::beat line for the new beat.");
        }

        // The row starts archived and the prose write brings it back: its ::beat line is then the only thing that places it.
        var versions = await prose.UpdateManyAsync([(sceneId, request.Version, current => Conserved(current, body))], EditorOrigin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) =>
                {
                    if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "SELECT EXISTS (SELECT 1 FROM beat WHERE id = @beatId)", new { beatId = request.BeatId }, transaction, cancellationToken: ct)))
                    {
                        throw ApiException.Conflict("A beat with that id already exists.");
                    }

                    await connection.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO beat (id, scene_id, archived_at) SELECT @beatId, id, now() FROM scene WHERE id = @sceneId",
                        new { beatId = request.BeatId, sceneId }, transaction, cancellationToken: ct));
                },
            },
            cancellationToken);
        return new InsertBeatResult(request.BeatId, versions[0]);
    }

    public async Task<BeatDto> PatchBeatAsync(Guid beatId, PatchBeatRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Beat, beatId, BookRole.Write, cancellationToken, "Beat not found.");
        if (request.TargetWordCount is <= 0)
        {
            throw ApiException.Validation("targetWordCount must be positive.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE beat SET body = coalesce(@body, body), is_hidden = coalesce(@isHidden, is_hidden),
                            target_word_count = coalesce(@target, target_word_count), title = coalesce(@title, title), updated_at = now()
            WHERE id = @beatId AND archived_at IS NULL
            """,
            new { beatId, body = request.Body, isHidden = request.IsHidden, target = request.TargetWordCount, title = request.Title?.Trim() }, cancellationToken: cancellationToken));
        if (updated == 0)
        {
            throw ApiException.NotFound("Beat not found.");
        }

        var bookId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT s.book_id FROM beat b JOIN scene s ON s.id = b.scene_id WHERE b.id = @beatId", new { beatId }, cancellationToken: cancellationToken));
        return (await ListAsync(bookId, cancellationToken)).Beats.Single(b => b.Id == beatId);
    }

    public async Task<SplitSceneResult> SplitAsync(Guid sceneId, SplitSceneRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.Write, cancellationToken, "Scene not found.");
        var head = request.HeadBody ?? throw ApiException.Validation("headBody is required.");
        var tail = request.TailBody ?? throw ApiException.Validation("tailBody is required.");
        var newSceneId = Guid.CreateVersion7();
        var tailBeats = BeatIds(tail);

        var original = "";
        var versions = await prose.UpdateManyAsync(
            [
                (sceneId, request.Version, current =>
                {
                    original = current;
                    return head;
                }),
                (newSceneId, 0, _ => ConservedAcross([original], [head, tail]) ? tail : throw Dropped()),
            ], EditorOrigin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) =>
                {
                    var scene = await connection.QuerySingleOrDefaultAsync<SceneRow>(new CommandDefinition(
                        "SELECT book_id AS BookId, chapter_id AS ChapterId, position FROM scene WHERE id = @sceneId AND archived_at IS NULL",
                        new { sceneId }, transaction, cancellationToken: ct))
                        ?? throw ApiException.NotFound("Scene not found.");
                    var next = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                        "SELECT min(position) FROM scene WHERE chapter_id = @ChapterId AND archived_at IS NULL AND position > @Position",
                        scene, transaction, cancellationToken: ct));
                    await connection.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO scene (id, book_id, chapter_id, position) VALUES (@newSceneId, @BookId, @ChapterId, @position)",
                        new { newSceneId, scene.BookId, scene.ChapterId, position = FractionalKey.Between(scene.Position, next) }, transaction, cancellationToken: ct));
                    await MoveBeatsAsync(connection, transaction, sceneId, newSceneId, tailBeats, ct);
                    await MoveEmbedsAsync(connection, transaction, sceneId, newSceneId, tail, ct);
                },
            },
            cancellationToken);
        return new SplitSceneResult(newSceneId, versions[0], versions[1]);
    }

    public async Task<MoveBlockResult> MoveAsync(MoveBlockRequest request, CancellationToken cancellationToken)
    {
        await access.RequireAsync(BookItem.Scene, request.FromSceneId, BookRole.Write, cancellationToken, "Scene not found.");
        await access.RequireAsync(BookItem.Scene, request.ToSceneId, BookRole.Write, cancellationToken, "Scene not found.");
        var fromBody = request.FromBody ?? throw ApiException.Validation("fromBody is required.");
        var toBody = request.ToBody ?? throw ApiException.Validation("toBody is required.");
        if (request.FromSceneId == request.ToSceneId)
        {
            throw ApiException.Validation("A move within one scene is an ordinary prose save.");
        }

        var moved = BeatIds(toBody);
        var before = new List<string>();
        var versions = await prose.UpdateManyAsync(
            [
                (request.FromSceneId, request.FromVersion, current =>
                {
                    before.Add(current);
                    return fromBody;
                }),
                (request.ToSceneId, request.ToVersion, current =>
                {
                    before.Add(current);
                    return ConservedAcross(before, [fromBody, toBody]) ? toBody : throw Dropped();
                }),
            ], EditorOrigin,
            new ProseWriteOptions
            {
                BeforeWrites = async (connection, transaction, ct) =>
                {
                    var books = (await connection.QueryAsync<Guid>(new CommandDefinition(
                        "SELECT DISTINCT book_id FROM scene WHERE id IN (@from, @to)",
                        new { from = request.FromSceneId, to = request.ToSceneId }, transaction, cancellationToken: ct))).ToList();
                    if (books.Count == 0)
                    {
                        throw ApiException.NotFound("Scene not found.");
                    }

                    if (books.Count > 1)
                    {
                        throw ApiException.Validation("Blocks move between scenes of the same book.");
                    }

                    await MoveBeatsAsync(connection, transaction, request.FromSceneId, request.ToSceneId, moved, ct);
                },
            },
            cancellationToken);
        return new MoveBlockResult(versions[0], versions[1]);
    }

    // The client sends whole bodies, so the server checks that no embed (beat, figure, kb addition...) present before
    // the change has silently vanished from the scenes involved.
    private static bool ConservedAcross(IEnumerable<string> before, IEnumerable<string> after)
    {
        var kept = after.SelectMany(Embeds).ToHashSet();
        return before.SelectMany(Embeds).All(kept.Contains);
    }

    private static string Conserved(string current, string next) =>
        ConservedAcross([current], [next]) ? next : throw Dropped();

    private static ApiException Dropped() => ApiException.Validation("The change would drop an embedded block (a beat, figure or addition) from the scene.");

    private static IEnumerable<(ProseEmbedKind, Guid)> Embeds(string body) =>
        ProseDocument.Parse(body).ListEmbeds().Select(e => (e.Kind, e.Id));

    private static List<Guid> BeatIds(string body) =>
        ProseDocument.Parse(body).ListEmbeds().Where(e => e.Kind == ProseEmbedKind.Beat).Select(e => e.Id).ToList();

    // What the text below the split points at goes with it: Codex additions, figures and source uses. The second scene
    // also gets copies of the first one's labels and Knowledge base links, and no summary (it starts empty).
    private static async Task MoveEmbedsAsync(DbConnection connection, DbTransaction transaction, Guid from, Guid to, string tail, CancellationToken ct)
    {
        var embeds = ProseDocument.Parse(tail).ListEmbeds();
        var additions = embeds.Where(e => e.Kind == ProseEmbedKind.KbAddition).Select(e => e.Id).Distinct().ToList();
        var figures = embeds.Where(e => e.Kind == ProseEmbedKind.Figure).Select(e => e.Id).Distinct().ToList();
        var uses = embeds.Where(e => e.Kind == ProseEmbedKind.Ref).Select(e => e.Id).Distinct().ToList();
        var args = new { from, to, additions, figures, uses };
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE kb_progression SET scene_id = @to, updated_at = now() WHERE scene_id = @from AND id = ANY(@additions)",
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE figure SET scene_id = @to, updated_at = now() WHERE scene_id = @from AND id = ANY(@figures)",
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE reference_use SET scene_id = @to, updated_at = now() WHERE scene_id = @from AND id = ANY(@uses)",
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO scene_label (scene_id, label_id) SELECT @to, sl.label_id FROM scene_label sl JOIN label l ON l.id = sl.label_id AND l.archived_at IS NULL WHERE sl.scene_id = @from AND sl.archived_at IS NULL ON CONFLICT (scene_id, label_id) DO UPDATE SET archived_at = NULL",
            args, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO scene_kb (scene_id, kb_entry_id) SELECT @to, kb_entry_id FROM scene_kb WHERE scene_id = @from AND archived_at IS NULL
            ON CONFLICT (scene_id, kb_entry_id) DO UPDATE SET archived_at = NULL
            """,
            args, transaction, cancellationToken: ct));
    }

    // Only rows that still belong to the source move; a beat already in the destination stays.
    private static Task<int> MoveBeatsAsync(DbConnection connection, DbTransaction transaction, Guid from, Guid to, List<Guid> beatIds, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "UPDATE beat SET scene_id = @to, updated_at = now() WHERE scene_id = @from AND id = ANY(@beatIds)",
            new { from, to, beatIds }, transaction, cancellationToken: ct));
}
