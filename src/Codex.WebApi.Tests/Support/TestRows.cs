using Codex.Contracts;
using Dapper;
using Npgsql;

namespace Codex.WebApi.Tests;

// Every row an API test inserts straight into its cloned database, in one place: add new inserts here.
// Use with SchemaApiFixture (bare schema plus JD) or on a seed clone. Gotchas: inserting a book creates its first act
// at position a0; any act/chapter/scene/prose write bumps book.last_edited_at, so stamp recency last; prose_sync_embeds
// archives a beat, kb_progression or reference_use whose line or link is missing from the saved prose.
public static class TestRows
{
    // The one user the app acts as; saving prose records it in prose.updated_by. A no-op once a seed provides it.
    public static Task EnsureUserAsync(NpgsqlConnection db)
        => db.ExecuteAsync(
            "INSERT INTO app_user (id, email, display_name) VALUES (@id, 'jd@example.test', 'JD') ON CONFLICT DO NOTHING",
            new { id = SeedIds.Users.Jd });

    // A book; in a series it takes the next number.
    public static async Task<Guid> BookAsync(NpgsqlConnection db, string title = "Test book", Guid? seriesId = null)
    {
        await EnsureUserAsync(db);
        return await db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO book (owner_id, title, series_id, series_number)
            VALUES (@owner, @title, @seriesId,
                    CASE WHEN @seriesId::uuid IS NULL THEN NULL
                         ELSE (SELECT coalesce(max(series_number), 0) + 1 FROM book WHERE series_id = @seriesId) END)
            RETURNING id
            """,
            new { owner = SeedIds.Users.Jd, title, seriesId });
    }

    public static async Task<Guid> SeriesAsync(NpgsqlConnection db, string title = "Test series", string? description = null)
    {
        await EnsureUserAsync(db);
        return await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO book (kind, owner_id, title, description) VALUES ('series', @owner, @title, @description) RETURNING id",
            new { owner = SeedIds.Users.Jd, title, description });
    }

    // A Library row: the book's header fields, `words` words of prose in one scene, then its recency and archive stamp.
    public static async Task<Guid> ShelfBookAsync(
        NpgsqlConnection db,
        string title,
        string? subtitle = null,
        Guid? seriesId = null,
        int? number = null,
        string status = "planning",
        int? target = null,
        int words = 0,
        TimeSpan? edited = null,
        bool archived = false)
    {
        await EnsureUserAsync(db);
        var id = await db.QuerySingleAsync<Guid>(
            """
            INSERT INTO book (kind, owner_id, title, subtitle, author_name, series_id, series_number, status, target_word_count)
            VALUES ('book', @owner, @title, @subtitle, 'Joel Dickson', @seriesId, @number, @status, @target)
            RETURNING id
            """,
            new { owner = SeedIds.Users.Jd, title, subtitle, seriesId, number, status, target });
        if (words > 0)
        {
            var sceneId = await SceneAsync(db, id, await ChapterAsync(db, id, await FirstActAsync(db, id), "a0", "Ch"), "a0");
            await ProseAsync(db, sceneId, string.Join(' ', Enumerable.Repeat("word", words)));
        }

        await db.ExecuteAsync(
            """
            UPDATE book SET last_edited_at = now() - make_interval(secs => @seconds),
                            archived_at = CASE WHEN @archived THEN now() END
            WHERE id = @id
            """,
            new { id, seconds = (edited ?? TimeSpan.Zero).TotalSeconds, archived });
        return id;
    }

    // The act every book is created with.
    public static Task<Guid> FirstActAsync(NpgsqlConnection db, Guid bookId)
        => db.ExecuteScalarAsync<Guid>("SELECT id FROM act WHERE book_id = @bookId ORDER BY position LIMIT 1", new { bookId });

    public static Task<Guid> ActAsync(NpgsqlConnection db, Guid bookId, string position, string title = "")
        => db.ExecuteScalarAsync<Guid>(
            "INSERT INTO act (book_id, position, title) VALUES (@bookId, @position, @title) RETURNING id",
            new { bookId, position, title });

    public static Task<Guid> ChapterAsync(
        NpgsqlConnection db, Guid bookId, Guid actId, string position, string title, bool isNumbered = true)
        => db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO chapter (book_id, act_id, position, title, is_numbered)
            VALUES (@bookId, @actId, @position, @title, @isNumbered) RETURNING id
            """,
            new { bookId, actId, position, title, isNumbered });

    public static Task<Guid> SceneAsync(
        NpgsqlConnection db, Guid bookId, Guid chapterId, string position, string? title = null, string? summary = null,
        string status = "outline", bool archived = false)
        => db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO scene (book_id, chapter_id, position, title, summary, status, archived_at)
            VALUES (@bookId, @chapterId, @position, @title, @summary, @status, CASE WHEN @archived THEN now() END) RETURNING id
            """,
            new { bookId, chapterId, position, title, summary, status, archived });

    public static Task ProseAsync(NpgsqlConnection db, Guid sceneId, string body, int version = 1)
        => db.ExecuteAsync(
            "INSERT INTO prose (scene_id, body, version) VALUES (@sceneId, @body, @version)",
            new { sceneId, body, version });

    // Replaces a scene's prose body in SQL (the triggers run; the version is not bumped).
    public static Task SetProseAsync(NpgsqlConnection db, Guid sceneId, string body)
        => db.ExecuteAsync("UPDATE prose SET body = @body WHERE scene_id = @sceneId", new { sceneId, body });

    // One book with one chapter holding one scene; the shape most tests need.
    public static async Task<(Guid BookId, Guid ChapterId, Guid SceneId)> SingleSceneAsync(NpgsqlConnection db)
    {
        var bookId = await BookAsync(db);
        var chapterId = await ChapterAsync(db, bookId, await FirstActAsync(db, bookId), "a0", "Chapter");
        var sceneId = await SceneAsync(db, bookId, chapterId, "a0");
        return (bookId, chapterId, sceneId);
    }

    public static Task<Guid> LabelAsync(NpgsqlConnection db, Guid scopeId, string name, string position = "a0", string? color = null)
        => db.ExecuteScalarAsync<Guid>(
            "INSERT INTO label (book_id, name, color, position) VALUES (@scopeId, @name, @color, @position) RETURNING id",
            new { scopeId, name, color, position });

    public static Task<Guid> BeatAsync(NpgsqlConnection db, Guid sceneId)
        => db.QuerySingleAsync<Guid>("INSERT INTO beat (scene_id, body) VALUES (@sceneId, 'beat') RETURNING id", new { sceneId });

    public static Task<Guid> CategoryAsync(NpgsqlConnection db, Guid bookId, string name, string position = "a0", string? color = null)
        => db.QuerySingleAsync<Guid>(
            "INSERT INTO kb_category (book_id, name, position, color) VALUES (@bookId, @name, @position, @color) RETURNING id",
            new { bookId, name, position, color });

    public static Task<Guid> EntryAsync(
        NpgsqlConnection db, Guid bookId, Guid categoryId, string name, string position = "a0", string[]? aliases = null, bool always = false)
        => db.QuerySingleAsync<Guid>(
            """
            INSERT INTO kb_entry (book_id, category_id, name, position, aliases, always_in_context)
            VALUES (@bookId, @categoryId, @name, @position, @aliases, @always) RETURNING id
            """,
            new { bookId, categoryId, name, position, aliases = aliases ?? [], always });

    // A web source whose citation is complete (so it can clear once signed off).
    public static Task<Guid> SourceAsync(NpgsqlConnection db, Guid bookId, string title, string licence = "cc_by_4")
        => db.QuerySingleAsync<Guid>(
            """
            INSERT INTO reference (book_id, kind, title, url, accessed_on, licence)
            VALUES (@bookId, 'web', @title, 'https://example.test/' || @title, @today, @licence) RETURNING id
            """,
            new { bookId, title, licence, today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime) });

    public static Task SignOffAsync(NpgsqlConnection db, Guid referenceId)
        => db.ExecuteAsync(
            "UPDATE reference SET signed_off_at = now(), signed_off_by = @user WHERE id = @referenceId",
            new { referenceId, user = SeedIds.Users.Jd });

    public static Task SetQuoteLimitAsync(NpgsqlConnection db, Guid bookId, int limit)
        => db.ExecuteAsync(
            "INSERT INTO publication_settings (book_id, quote_word_limit) VALUES (@bookId, @limit)", new { bookId, limit });

    // Insert the use before its link is in the prose, or saving the prose archives it.
    public static Task<Guid> UseAsync(
        NpgsqlConnection db, Guid bookId, Guid referenceId, Guid sceneId, string excerpt, string kind = "quote", Guid? generationId = null)
        => db.QuerySingleAsync<Guid>(
            """
            INSERT INTO reference_use (book_id, reference_id, scene_id, kind, excerpt, generation_id)
            VALUES (@bookId, @referenceId, @sceneId, @kind, @excerpt, @generationId) RETURNING id
            """,
            new { bookId, referenceId, sceneId, kind, excerpt, generationId });

    // An asset row of a book or series (no stored file, no rendition job). Images get a pixel size, as the schema requires.
    public static Task<Guid> AssetAsync(NpgsqlConnection db, Guid bookId, string kind = "image", bool archived = false)
        => db.QuerySingleAsync<Guid>(
            """
            INSERT INTO asset (owner_id, book_id, kind, filename, mime, bytes, width_px, height_px, storage_key, archived_at)
            VALUES (@owner, @bookId, @kind, 'cover.png', 'image/png', 1,
                    CASE WHEN @kind = 'image' THEN 1600 END, CASE WHEN @kind = 'image' THEN 2400 END,
                    'test/' || gen_random_uuid(), CASE WHEN @archived THEN now() END)
            RETURNING id
            """,
            new { owner = SeedIds.Users.Jd, bookId, kind, archived });

    // The web rendition row the worker would write (no stored file).
    public static Task WebRenditionAsync(NpgsqlConnection db, Guid assetId)
        => db.ExecuteAsync(
            """
            INSERT INTO asset_rendition (asset_id, purpose, mime, width_px, height_px, bytes, storage_key)
            VALUES (@assetId, 'web', 'image/webp', 1067, 1600, 1, 'test/' || gen_random_uuid())
            """,
            new { assetId });

    // A user's layout template with one published 1.0.0 version and no stored package (enough for ownership checks).
    public static async Task<(Guid TemplateId, Guid VersionId)> UserTemplateAsync(NpgsqlConnection db, Guid owner, string slug = "my-template")
    {
        var templateId = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO layout_template (owner_id, slug, name) VALUES (@owner, @slug, @slug) RETURNING id", new { owner, slug });
        var versionId = await db.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO layout_template_version (template_id, major, minor, patch, engine, manifest, published_at, created_by)
            VALUES (@templateId, 1, 0, 0, 'tectonic', '{"trims": ["6x9"]}', now(), @owner) RETURNING id
            """,
            new { templateId, owner });
        return (templateId, versionId);
    }

    // A grant (#80): userId may open bookId (a book or a series) at role ("view", "comment" or "write"), granted by JD.
    public static Task GrantAsync(NpgsqlConnection db, Guid bookId, Guid userId, string role)
        => db.ExecuteAsync(
            """
            INSERT INTO book_grant (book_id, user_id, role, granted_by) VALUES (@bookId, @userId, @role, @jd)
            ON CONFLICT (book_id, user_id) DO UPDATE SET role = EXCLUDED.role
            """,
            new { bookId, userId, role, jd = SeedIds.Users.Jd });
}
