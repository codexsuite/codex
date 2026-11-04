using System.Data.Common;
using System.Diagnostics;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// Waits for the in-process rendition worker to finish every image an import stored for a book, by polling its jobs.
public static class ImportRenditions
{
    public static async Task SettledAsync(DbConnection db, Guid bookId, TimeSpan patience)
    {
        var clock = Stopwatch.StartNew();
        while (await db.ExecuteScalarAsync<int>(
                   """
                   SELECT count(*)::int FROM asset_rendition_job j JOIN asset a ON a.id = j.asset_id
                   WHERE a.book_id = @bookId AND j.status IN ('queued', 'running')
                   """,
                   new { bookId }) > 0)
        {
            clock.Elapsed.ShouldBeLessThan(patience, "the rendition worker did not finish the imported images");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
