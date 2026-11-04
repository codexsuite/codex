namespace Codex.WebApi.Tests.Shell;

// Shell scenarios start from the bare schema and insert every row they assert on (TestRows).
public abstract class ShellApiFixture : SchemaApiFixture
{
    protected async Task<Guid> AddSeriesAsync(string title, string? description = null)
    {
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.SeriesAsync(connection, title, description);
    }

    protected async Task<Guid> AddBookAsync(
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
        await using var connection = await OpenDatabaseAsync();
        return await TestRows.ShelfBookAsync(connection, title, subtitle, seriesId, number, status, target, words, edited, archived);
    }
}
