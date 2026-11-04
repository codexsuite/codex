using System.Net.Http.Json;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Ai;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

// R6 as the history shows it: the text a stale save from another tab overwrote is listed as "Replaced by another
// session", once, and Restore brings it back.
[TestFixture]
public class ProseReplacedRevisionTests : ApiFixture
{
    [Test]
    public async Task Put_WhenAnotherTabSavedFirst_ShouldListTheirTextAsReplacedAndLetItBeRestored()
    {
        Guid sceneId;
        await using (var db = await OpenDatabaseAsync())
        {
            var (_, _, scene) = await TestRows.SingleSceneAsync(db);
            sceneId = scene;
            await TestRows.ProseAsync(db, sceneId, "Seed text.");
        }

        // Tab 2 saves first (943-style), then tab 1 saves from the version it loaded.
        (await Client.PutAsJsonAsync($"/api/scenes/{sceneId}/prose", new SaveProseRequest("Tab two text.", 1))).EnsureSuccessStatusCode();
        (await Client.PutAsJsonAsync($"/api/scenes/{sceneId}/prose", new SaveProseRequest("Tab one text.", 1))).EnsureSuccessStatusCode();

        var history = await DraftStreams.GetAsync<ProseHistoryDto>(Client, $"/api/scenes/{sceneId}/prose/revisions");
        history.Revisions.Select(r => (r.Label, r.Body)).ShouldBe(
            [("Edited", "Tab one text."), ("Replaced by another session", "Tab two text.")]);
        history.Version.ShouldBe(3);

        var restore = await Client.PostAsync($"/api/scenes/{sceneId}/prose/revisions/{history.Revisions[1].Id}/restore", null);
        (await DraftStreams.ReadAsync<RestoreRevisionResult>(restore)).Body.ShouldBe("Tab two text.");
    }
}
