using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// Scenarios on the bare schema that build books with chapters, scenes, entries and sources (TestRows), each insert
// on its own connection.
public abstract class BookContentFixture : SchemaApiFixture
{
    protected async Task<Guid> AddSeriesAsync(string title)
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.SeriesAsync(connection, title);
    }

    protected async Task<Guid> AddBookAsync(string title, Guid? seriesId = null)
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.BookAsync(connection, title, seriesId);
    }

    protected async Task<Guid> AddCategoryAsync(Guid bookId, string name, string position = "a0", string? color = null)
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.CategoryAsync(connection, bookId, name, position, color);
    }

    protected async Task<Guid> AddEntryAsync(
        Guid bookId, Guid categoryId, string name, string position = "a0", string[]? aliases = null, bool always = false)
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.EntryAsync(connection, bookId, categoryId, name, position, aliases, always);
    }

    protected async Task<Guid> AddChapterAsync(Guid bookId, string position = "a0")
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.ChapterAsync(connection, bookId, await TestRows.FirstActAsync(connection, bookId), position, "Ch");
    }

    protected async Task<Guid> AddSceneAsync(Guid bookId, Guid chapterId, string position = "a0", string body = "")
    {
        await using var connection = await OpenDatabaseAsync();
        var sceneId = await TestRows.SceneAsync(connection, bookId, chapterId, position);
        await TestRows.ProseAsync(connection, sceneId, body);
        return sceneId;
    }

    protected async Task<Guid> AddBeatAsync(Guid sceneId)
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.BeatAsync(connection, sceneId);
    }

    protected async Task<Guid> AddSourceAsync(Guid bookId, string title, string licence = "cc_by_4")
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.SourceAsync(connection, bookId, title, licence);
    }

    protected async Task SignOffAsync(Guid referenceId)
    {
        await using var connection = await OpenDatabaseAsync();
        await TestRows.SignOffAsync(connection, referenceId);
    }

    protected async Task SetQuoteLimitAsync(Guid bookId, int limit)
    {
        await using var connection = await OpenDatabaseAsync();
        await TestRows.SetQuoteLimitAsync(connection, bookId, limit);
    }

    protected async Task<Guid> AddUseAsync(
        Guid bookId, Guid referenceId, Guid sceneId, string excerpt, string kind = "quote", Guid? generationId = null)
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.UseAsync(connection, bookId, referenceId, sceneId, excerpt, kind, generationId);
    }

    protected async Task SetProseAsync(Guid sceneId, string body)
    {
        await using var connection = await OpenDatabaseAsync();
        await TestRows.SetProseAsync(connection, sceneId, body);
    }

    protected async Task<Guid[]> SceneLinksAsync(Guid sceneId)
    {
        await using var connection = await OpenDatabaseAsync();
        return (await connection.QueryAsync<Guid>(
            "SELECT kb_entry_id FROM scene_kb WHERE scene_id = @sceneId AND archived_at IS NULL ORDER BY 1", new { sceneId })).ToArray();
    }
}
