using Agoda.IoC.Core;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Dapper;
using Npgsql;

namespace Codex.Application.Features.Structure;

[RegisterSingleton]
public sealed class OutlineQuery(NpgsqlDataSource dataSource, IBookAccess access)
{
    private sealed record ActRow(Guid Id, string Title, string Position, Guid? OpenerAssetId, string? OpenerCaption, bool OpenerHasWeb);

    private sealed record ChapterRow(
        Guid Id, Guid ActId, int? Number, string Title, string Slug, string Position, bool Archived, int ArchivedSceneCount,
        Guid? OpenerAssetId, string? OpenerCaption, bool OpenerHasWeb);

    private sealed record SceneRow(
        Guid Id, Guid ChapterId, int? ChapterNumber, int SceneNumber, string? Title, string? Summary, string Status,
        string Position, int Words, bool HasPendingDraft);

    private sealed record SceneLabelRow(Guid SceneId, Guid Id, string Name, string? Color);

    private sealed record SceneEntryRow(Guid SceneId, Guid Id, string Name);

    public async Task<OutlineDto> GetAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(bookId, BookRole.View, cancellationToken, notFound: "Book not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM book WHERE id = @bookId AND kind = 'book')", new { bookId }, cancellationToken: cancellationToken));
        if (!exists)
        {
            throw ApiException.NotFound("Book not found.");
        }

        var acts = (await connection.QueryAsync<ActRow>(new CommandDefinition(
            """
            SELECT a.id, a.title, a.position, a.opener_asset_id AS OpenerAssetId, a.opener_caption AS OpenerCaption,
                   EXISTS (SELECT 1 FROM asset oa JOIN asset_rendition r ON r.asset_id = oa.id AND r.purpose = 'web'
                           WHERE oa.id = a.opener_asset_id AND oa.archived_at IS NULL) AS OpenerHasWeb
            FROM act a WHERE a.book_id = @bookId AND a.archived_at IS NULL ORDER BY a.position, a.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();
        var chapters = (await connection.QueryAsync<ChapterRow>(new CommandDefinition(
            """
            SELECT c.id, c.act_id AS ActId, co.chapter_number::int AS number, c.title, c.slug, c.position,
                   c.archived_at IS NOT NULL AS archived,
                   (SELECT count(*) FROM scene s WHERE s.chapter_id = c.id AND s.archived_at IS NOT NULL)::int AS ArchivedSceneCount,
                   c.opener_asset_id AS OpenerAssetId, c.opener_caption AS OpenerCaption,
                   EXISTS (SELECT 1 FROM asset oa JOIN asset_rendition r ON r.asset_id = oa.id AND r.purpose = 'web'
                           WHERE oa.id = c.opener_asset_id AND oa.archived_at IS NULL) AS OpenerHasWeb
            FROM chapter c
            JOIN act a ON a.id = c.act_id
            LEFT JOIN chapter_outline co ON co.id = c.id
            WHERE c.book_id = @bookId AND a.archived_at IS NULL
            ORDER BY a.position, c.position, c.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToList();
        var scenes = await LoadScenesAsync(connection, bookId, null, cancellationToken);

        var scenesByChapter = scenes.ToLookup(s => s.ChapterId);
        var chaptersByAct = chapters.ToLookup(c => c.ActId);
        return new OutlineDto(bookId, acts.ConvertAll(a => new OutlineActDto(
            a.Id,
            a.Title,
            a.Position,
            chaptersByAct[a.Id].Select(c => new OutlineChapterDto(
                c.Id, c.Number, c.Title, c.Slug, c.Position, c.Archived, c.ArchivedSceneCount,
                scenesByChapter[c.Id].Select(s => s.Scene).ToList(),
                Opener(c.OpenerAssetId, c.OpenerCaption, c.OpenerHasWeb))).ToList(),
            Opener(a.OpenerAssetId, a.OpenerCaption, a.OpenerHasWeb))));
    }

    // The thumbnail is the web rendition of a live image; an archived or unrendered one has none.
    private static OutlineOpenerDto? Opener(Guid? assetId, string? caption, bool hasWeb)
    {
        if (assetId is not { } id)
        {
            return null;
        }

        return new OutlineOpenerDto(id, caption, hasWeb ? $"/api/assets/{id}/renditions/web" : null);
    }

    // One scene as the outline shows it (the PATCH response).
    public async Task<OutlineSceneDto> GetSceneAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        await access.RequireAsync(BookItem.Scene, sceneId, BookRole.View, cancellationToken, notFound: "Scene not found.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var bookId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT book_id FROM scene WHERE id = @sceneId AND archived_at IS NULL", new { sceneId }, cancellationToken: cancellationToken))
            ?? throw ApiException.NotFound("Scene not found.");
        var scenes = await LoadScenesAsync(connection, bookId, sceneId, cancellationToken);
        return scenes.Single().Scene;
    }

    private static async Task<List<(Guid ChapterId, OutlineSceneDto Scene)>> LoadScenesAsync(
        NpgsqlConnection connection, Guid bookId, Guid? sceneId, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<SceneRow>(new CommandDefinition(
            """
            WITH numbered AS (
                SELECT s.id, s.chapter_id, co.chapter_number::int AS chapter_number,
                       (row_number() OVER (PARTITION BY s.chapter_id ORDER BY s.position, s.id))::int AS scene_number,
                       s.title, s.summary, s.status, s.position, coalesce(p.word_count, 0) AS words,
                       EXISTS (SELECT 1 FROM ai_generation g WHERE g.scene_id = s.id AND g.status = 'pending') AS has_pending_draft,
                       a.position AS act_position, c.position AS chapter_position
                FROM scene s
                JOIN chapter c ON c.id = s.chapter_id
                JOIN act a ON a.id = c.act_id
                LEFT JOIN chapter_outline co ON co.id = c.id
                LEFT JOIN prose p ON p.scene_id = s.id
                WHERE s.book_id = @bookId AND s.archived_at IS NULL AND a.archived_at IS NULL
            )
            SELECT id, chapter_id AS ChapterId, chapter_number AS ChapterNumber, scene_number AS SceneNumber, title, summary, status, position, words,
                   has_pending_draft AS HasPendingDraft
            FROM numbered
            WHERE @sceneId::uuid IS NULL OR id = @sceneId::uuid
            ORDER BY act_position, chapter_position, chapter_id, position, id
            """,
            new { bookId, sceneId }, cancellationToken: cancellationToken));
        var labels = (await connection.QueryAsync<SceneLabelRow>(new CommandDefinition(
            """
            SELECT sl.scene_id AS SceneId, l.id, l.name, l.color
            FROM scene_label sl
            JOIN label l ON l.id = sl.label_id AND l.archived_at IS NULL
            JOIN scene s ON s.id = sl.scene_id
            WHERE s.book_id = @bookId AND sl.archived_at IS NULL
            ORDER BY l.position, l.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToLookup(l => l.SceneId);
        var entries = (await connection.QueryAsync<SceneEntryRow>(new CommandDefinition(
            """
            SELECT sk.scene_id AS SceneId, e.id, e.name
            FROM scene_kb sk
            JOIN kb_entry e ON e.id = sk.kb_entry_id
            JOIN scene s ON s.id = sk.scene_id
            WHERE s.book_id = @bookId AND sk.archived_at IS NULL AND e.archived_at IS NULL
            ORDER BY e.name, e.id
            """,
            new { bookId }, cancellationToken: cancellationToken))).ToLookup(e => e.SceneId);

        return rows.Select(r => (r.ChapterId, new OutlineSceneDto(
            r.Id,
            r.ChapterNumber == null ? r.SceneNumber.ToString() : $"{r.ChapterNumber}.{r.SceneNumber}",
            r.Title,
            r.Summary,
            r.Status,
            r.Position,
            r.Words,
            r.HasPendingDraft,
            labels[r.Id].Select(l => new LabelRefDto(l.Id, l.Name, l.Color)).ToList(),
            entries[r.Id].Select(e => new EntryRefDto(e.Id, e.Name)).ToList()))).ToList();
    }
}
