using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

// Split a scene (Write's "Split scene here", J16 step 6): the second part starts with no summary, copies the first
// part's labels and Knowledge base links, and takes the beats and source uses that sit below the split.
[TestFixture]
public class SceneSplitTests : SchemaApiFixture
{
    [Test]
    public async Task Split_ShouldCopyLabelsAndLinksAndMoveWhatSitsBelow()
    {
        await using var db = await OpenDatabaseAsync();
        var (bookId, _, sceneId) = await TestRows.SingleSceneAsync(db);
        await db.ExecuteAsync("UPDATE scene SET summary = 'Kept.' WHERE id = @sceneId", new { sceneId });
        var label = await TestRows.LabelAsync(db, bookId, "Drafting");
        var entry = await TestRows.EntryAsync(db, bookId, await TestRows.CategoryAsync(db, bookId, "People"), "Ada");
        await db.ExecuteAsync("INSERT INTO scene_label (scene_id, label_id) VALUES (@sceneId, @label)", new { sceneId, label });
        await db.ExecuteAsync("INSERT INTO scene_kb (scene_id, kb_entry_id) VALUES (@sceneId, @entry)", new { sceneId, entry });
        var source = await TestRows.SourceAsync(db, bookId, "Manifesto");
        var use = await TestRows.UseAsync(db, bookId, source, sceneId, "quoted");
        var beat = await TestRows.BeatAsync(db, sceneId);
        var head = "Top.\n";
        var tail = $"Below [quoted](ref:{use}).\n\n::beat{{id=\"{beat}\"}}\n";
        await TestRows.ProseAsync(db, sceneId, head + "\n" + tail);

        var response = await Client.PostAsJsonAsync($"/api/scenes/{sceneId}/split", new SplitSceneRequest(head, tail, 1));

        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<SplitSceneResult>(JsonSerializerOptions.Web))!;
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM reference_use WHERE id = @use", new { use })).ShouldBe(result.NewSceneId);
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM beat WHERE id = @beat", new { beat })).ShouldBe(result.NewSceneId);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene_label WHERE scene_id IN (@sceneId, @id)", new { sceneId, id = result.NewSceneId })).ShouldBe(2);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene_kb WHERE scene_id IN (@sceneId, @id) AND archived_at IS NULL", new { sceneId, id = result.NewSceneId })).ShouldBe(2);
        (await db.ExecuteScalarAsync<string?>("SELECT summary FROM scene WHERE id = @id", new { id = result.NewSceneId })).ShouldBeNull();
        (await db.ExecuteScalarAsync<string?>("SELECT summary FROM scene WHERE id = @sceneId", new { sceneId })).ShouldBe("Kept.");
    }
}
