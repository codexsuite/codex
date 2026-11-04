using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// An entry archived while a link to it is being written answers 404 (or the link wins first), never a 500.
[TestFixture]
public class KbLinksArchiveRaceTests : BookContentFixture
{
    [Test, CancelAfter(30000)]
    public async Task Post_WhenEntryArchivedConcurrently_ShouldNeverFailWithServerError()
    {
        var book = await AddBookAsync("Tides");
        var category = await AddCategoryAsync(book, "People");
        var scene = await AddSceneAsync(book, await AddChapterAsync(book));
        var entries = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            entries.Add(await AddEntryAsync(book, category, $"Entry {i}", $"a{i:00}"));
        }

        var statuses = await Task.WhenAll(entries.Select(async entry =>
        {
            var link = Client.PostAsJsonAsync($"/api/scenes/{scene}/kb", new LinkKbEntryRequest(entry));
            await using var connection = await OpenDatabaseAsync();
            await connection.ExecuteAsync("UPDATE kb_entry SET archived_at = now() WHERE id = @entry", new { entry });
            return (await link).StatusCode;
        }));

        statuses.ShouldAllBe(s => s == HttpStatusCode.NoContent || s == HttpStatusCode.NotFound);
    }
}
