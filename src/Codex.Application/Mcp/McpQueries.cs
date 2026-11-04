using Agoda.IoC.Core;
using Codex.Contracts;
using Dapper;
using Npgsql;

namespace Codex.Application.Mcp;

// The reads MCP needs that no screen has a query for: where the author left off, which book a scene or draft is in,
// and the Library-wide search behind `search`. Scoped like every other query to the books the current user can open
// (#80: book_access, owned and shared alike); these only find things, at any role, and the tool that writes checks its
// own minimum role through IBookAccess.
[RegisterSingleton]
public sealed class McpQueries(NpgsqlDataSource dataSource, ICurrentUser currentUser)
{
    public sealed record SceneProse(Guid SceneId, int Version, string Body);

    // kind: book, chapter, scene, kb_entry, source, prose. BookId is the book the hit lives in (for an entry, the book
    // or series that owns it); Text is the matched body for prose hits. LinkBookId, for an entry: the book its open_url
    // opens in (the searched book, else the most recently edited live book of the owner's scope that the caller can
    // open; null for a series with no such book).
    public sealed record SearchRow(string Kind, Guid Id, Guid BookId, string Label, string? Text, Guid? LinkBookId);

    // The scenes of a book that have plan notes, by id (the notes are not on the outline the screens share).
    public async Task<IReadOnlyDictionary<Guid, string>> ScenePlanNotesAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<(Guid Id, string Notes)>(new CommandDefinition(
            "SELECT s.id, s.plan_notes FROM scene s JOIN book_access a ON a.book_id = s.book_id AND a.user_id = @user WHERE s.book_id = @bookId AND s.archived_at IS NULL AND s.plan_notes IS NOT NULL",
            new { bookId, user = currentUser.Id }, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.Id, r => r.Notes);
    }

    public sealed record ChapterText(string? Summary, string? Notes);

    // The live chapters of a book that have a summary or plan notes, by id (the outline the screens share carries neither).
    public async Task<IReadOnlyDictionary<Guid, ChapterText>> ChapterTextsAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<(Guid Id, string? Summary, string? Notes)>(new CommandDefinition(
            "SELECT c.id, c.summary, c.notes FROM chapter c JOIN book_access a ON a.book_id = c.book_id AND a.user_id = @user WHERE c.book_id = @bookId AND (c.summary IS NOT NULL OR c.notes IS NOT NULL)",
            new { bookId, user = currentUser.Id }, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.Id, r => new ChapterText(r.Summary, r.Notes));
    }

    // The scene the author was last working on (proposed): the one whose pending AI draft is newest, else the one
    // whose prose changed last; ties in reading order.
    public async Task<Guid?> LastSceneAsync(Guid bookId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT s.id
            FROM scene s
            JOIN chapter c ON c.id = s.chapter_id AND c.archived_at IS NULL
            JOIN act a ON a.id = c.act_id AND a.archived_at IS NULL
            LEFT JOIN prose p ON p.scene_id = s.id
            LEFT JOIN LATERAL (SELECT max(g.created_at) AS at FROM ai_generation g
                               WHERE g.scene_id = s.id AND g.status = 'pending') d ON true
            WHERE s.book_id = @bookId AND s.archived_at IS NULL
            ORDER BY d.at IS NULL, d.at DESC, p.updated_at DESC NULLS LAST, a.position, c.position, s.position
            LIMIT 1
            """,
            new { bookId }, cancellationToken: cancellationToken));
    }

    public async Task<SceneProse> SceneProseAsync(Guid sceneId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<SceneProse>(new CommandDefinition(
            "SELECT s.id AS SceneId, coalesce(p.version, 0) AS Version, coalesce(p.body, '') AS Body FROM scene s LEFT JOIN prose p ON p.scene_id = s.id WHERE s.id = @sceneId",
            new { sceneId }, cancellationToken: cancellationToken));
    }

    // The book a scene or an AI generation belongs to, when the current user can open it; null otherwise.
    public async Task<Guid?> BookOfAsync(Guid? sceneId, Guid? generationId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT a.book_id FROM book_access a
            WHERE a.user_id = @user
              AND a.book_id = coalesce((SELECT book_id FROM scene WHERE id = @sceneId::uuid),
                                       (SELECT book_id FROM ai_generation WHERE id = @generationId::uuid))
            """,
            new { user = currentUser.Id, sceneId, generationId }, cancellationToken: cancellationToken));
    }

    public sealed record FigureRow(Guid Id, Guid SceneId, Guid AssetId, string Placement, string? Caption, string? AltText);

    // A live figure in a book the current user can open; null when there is none.
    public async Task<FigureRow?> FigureAsync(Guid figureId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<FigureRow>(new CommandDefinition(
            """
            SELECT f.id, f.scene_id AS SceneId, f.asset_id AS AssetId, f.placement, f.caption, f.alt_text AS AltText
            FROM figure f JOIN scene s ON s.id = f.scene_id JOIN book_access a ON a.book_id = s.book_id AND a.user_id = @user
            WHERE f.id = @figureId AND f.archived_at IS NULL
            """,
            new { figureId, user = currentUser.Id }, cancellationToken: cancellationToken));
    }

    // The book of an act or chapter, when the current user can open it; null otherwise.
    public async Task<Guid?> BookOfActOrChapterAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT a.book_id FROM book_access a
            WHERE a.user_id = @user
              AND a.book_id = coalesce((SELECT book_id FROM act WHERE id = @id), (SELECT book_id FROM chapter WHERE id = @id))
            """,
            new { id, user = currentUser.Id }, cancellationToken: cancellationToken));
    }

    // The scene of a live beat in a book the current user can open; null when there is none.
    public async Task<Guid?> SceneOfBeatAsync(Guid beatId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT s.id FROM beat bt JOIN scene s ON s.id = bt.scene_id JOIN book_access a ON a.book_id = s.book_id AND a.user_id = @user
            WHERE bt.id = @beatId AND bt.archived_at IS NULL
            """,
            new { beatId, user = currentUser.Id }, cancellationToken: cancellationToken));
    }

    // Whether the scene is already linked to the entry (a live scene_kb row).
    public async Task<bool> SceneLinksEntryAsync(Guid sceneId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM scene_kb WHERE scene_id = @sceneId AND kb_entry_id = @entryId AND archived_at IS NULL)",
            new { sceneId, entryId }, cancellationToken: cancellationToken));
    }

    // The chapter of one of the book's archived scenes; null when the book has no such archived scene.
    public async Task<Guid?> ArchivedSceneChapterAsync(Guid bookId, Guid sceneId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT chapter_id FROM scene WHERE id = @sceneId AND book_id = @bookId AND archived_at IS NOT NULL",
            new { sceneId, bookId }, cancellationToken: cancellationToken));
    }

    // An archived chapter of the book, for edit_outline's restore: where it goes back and whether it counts in the numbering.
    public sealed record ArchivedChapter(Guid ActId, string Position, bool IsNumbered, bool ActArchived);

    public async Task<ArchivedChapter?> ArchivedChapterAsync(Guid bookId, Guid chapterId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<ArchivedChapter>(new CommandDefinition(
            """
            SELECT c.act_id AS ActId, c.position, c.is_numbered AS IsNumbered, a.archived_at IS NOT NULL AS ActArchived
            FROM chapter c JOIN act a ON a.id = c.act_id
            WHERE c.id = @chapterId AND c.book_id = @bookId AND c.archived_at IS NOT NULL
            """,
            new { chapterId, bookId }, cancellationToken: cancellationToken));
    }

    // An archived act of the book, for edit_outline's restore: its title, its key and its live chapters in order (Numbered says
    // which count in the numbering), so the batch can renumber the chapters after it. Null when there is no such act.
    public sealed record ArchivedActChapter(Guid Id, bool IsNumbered);

    public sealed record ArchivedAct(string Title, string Position, IReadOnlyList<ArchivedActChapter> Chapters);

    private sealed record ArchivedActRow(string Title, string Position);

    public async Task<ArchivedAct?> ArchivedActAsync(Guid bookId, Guid actId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var act = await connection.QuerySingleOrDefaultAsync<ArchivedActRow>(new CommandDefinition(
            "SELECT title, position FROM act WHERE id = @actId AND book_id = @bookId AND archived_at IS NOT NULL",
            new { actId, bookId }, cancellationToken: cancellationToken));
        if (act == null)
        {
            return null;
        }
        var chapters = await connection.QueryAsync<ArchivedActChapter>(new CommandDefinition(
            "SELECT id, is_numbered AS IsNumbered FROM chapter WHERE act_id = @actId AND archived_at IS NULL ORDER BY position, id",
            new { actId }, cancellationToken: cancellationToken));
        return new ArchivedAct(act.Title, act.Position, chapters.ToList());
    }

    // Live rows whose titles, summaries, names, aliases or prose contain `query` (case-insensitive), in kind order.
    public async Task<IReadOnlyList<SearchRow>> SearchAsync(
        string query, Guid? bookId, IReadOnlyCollection<string> kinds, int limit, CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(query)}%";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<SearchRow>(new CommandDefinition(
            """
            WITH books AS (
                SELECT b.id FROM book b JOIN book_access a ON a.book_id = b.id AND a.user_id = @user
                WHERE b.kind = 'book' AND b.archived_at IS NULL AND (@bookId::uuid IS NULL OR b.id = @bookId::uuid)
                  AND NOT EXISTS (SELECT 1 FROM book sr WHERE sr.id = b.series_id AND sr.archived_at IS NOT NULL)
            ),
            live_scenes AS (
                SELECT s.id, s.book_id, s.title, s.summary
                FROM scene s
                JOIN chapter c ON c.id = s.chapter_id AND c.archived_at IS NULL
                JOIN act a ON a.id = c.act_id AND a.archived_at IS NULL
                WHERE s.archived_at IS NULL AND s.book_id IN (SELECT id FROM books)
            ),
            hits AS (
                SELECT 1 AS rank, 'book' AS kind, b.id, b.id AS book_id, b.title AS label, NULL::text AS text, b.last_edited_at AS at,
                       NULL::uuid AS link_book_id
                FROM book b WHERE 'book' = ANY(@kinds) AND b.id IN (SELECT id FROM books)
                  AND (b.title ILIKE @pattern OR b.subtitle ILIKE @pattern)
                UNION ALL
                SELECT 2, 'chapter', c.id, c.book_id, c.title, NULL, c.updated_at, NULL
                FROM chapter c JOIN act a ON a.id = c.act_id AND a.archived_at IS NULL
                WHERE 'chapter' = ANY(@kinds) AND c.archived_at IS NULL AND c.book_id IN (SELECT id FROM books) AND c.title ILIKE @pattern
                UNION ALL
                SELECT 3, 'scene', s.id, s.book_id, coalesce(s.title, ''), NULL, NULL, NULL
                FROM live_scenes s WHERE 'scene' = ANY(@kinds) AND (s.title ILIKE @pattern OR s.summary ILIKE @pattern)
                UNION ALL
                SELECT 4, 'kb_entry', e.id, e.book_id, e.name, NULL, e.updated_at,
                       coalesce(@bookId::uuid,
                                (SELECT bk.id FROM book_scope bs JOIN book bk ON bk.id = bs.book_id
                                 JOIN book_access ba ON ba.book_id = bk.id AND ba.user_id = @user
                                 WHERE bs.scope_id = e.book_id AND bk.archived_at IS NULL
                                 ORDER BY bk.last_edited_at DESC, bk.id LIMIT 1))
                FROM kb_entry e
                WHERE 'kb_entry' = ANY(@kinds) AND e.archived_at IS NULL
                  AND e.book_id IN (SELECT bs.scope_id FROM book_scope bs WHERE bs.book_id IN (SELECT id FROM books))
                  AND (e.name ILIKE @pattern OR EXISTS (SELECT 1 FROM unnest(e.aliases) al WHERE al ILIKE @pattern))
                UNION ALL
                SELECT 5, 'source', r.id, r.book_id, r.title, NULL, r.updated_at, NULL
                FROM reference r
                WHERE 'source' = ANY(@kinds) AND r.archived_at IS NULL AND r.book_id IN (SELECT id FROM books) AND r.title ILIKE @pattern
                UNION ALL
                SELECT 6, 'prose', s.id, s.book_id, '', p.body, p.updated_at, NULL
                FROM live_scenes s JOIN prose p ON p.scene_id = s.id
                WHERE 'prose' = ANY(@kinds) AND p.body ILIKE @pattern
            )
            SELECT kind AS Kind, id AS Id, book_id AS BookId, label AS Label, text AS Text, link_book_id AS LinkBookId
            FROM hits ORDER BY rank, at DESC NULLS LAST, label, id
            LIMIT @limit
            """,
            new { user = currentUser.Id, bookId, kinds = kinds.ToArray(), pattern, limit },
            cancellationToken: cancellationToken))).ToList();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
