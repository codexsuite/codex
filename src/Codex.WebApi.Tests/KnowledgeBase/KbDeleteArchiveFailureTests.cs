using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// The archive step runs in the prose write's transaction: when archiving the entry fails after the scenes were
// rewritten, the scenes, the summary and the scene link stay as they were.
[TestFixture]
public class KbDeleteArchiveFailureTests : BookContentFixture
{
    private Guid _anna;
    private Guid _scene;
    private string _body = "";

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        var book = await AddBookAsync("Tides");
        _anna = await AddEntryAsync(book, await AddCategoryAsync(book, "People"), "Anna");
        _scene = await AddSceneAsync(book, await AddChapterAsync(book), "a0");
        _body = $"Hello [Anna](kb:{_anna}).";
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE prose SET body = @_body WHERE scene_id = @_scene", new { _body, _scene });
        await db.ExecuteAsync("UPDATE scene SET summary = @summary WHERE id = @_scene", new { summary = $"About [Anna](kb:{_anna}).", _scene });
        await db.ExecuteAsync("INSERT INTO scene_kb (scene_id, kb_entry_id) VALUES (@_scene, @_anna)", new { _scene, _anna });
        await db.ExecuteAsync(
            """
            CREATE FUNCTION refuse_archive() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN IF NEW.archived_at IS NOT NULL THEN RAISE EXCEPTION 'archive refused'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER refuse_archive BEFORE UPDATE ON kb_entry FOR EACH ROW EXECUTE FUNCTION refuse_archive();
            """);
    }

    [Test, Order(1)]
    public async Task Delete_WhenArchivingTheEntryFails_ShouldLeaveTheScenesAsTheyWere()
    {
        var response = await Client.DeleteAsync($"/api/kb/entries/{_anna}");

        (await ApiErrors.ReadApiErrorAsync(response, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene}/prose"))!;
        (prose.Version, prose.Body).ShouldBe((1, _body));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @_scene", new { _scene })).ShouldBe($"About [Anna](kb:{_anna}).");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene_kb WHERE kb_entry_id = @_anna AND archived_at IS NULL", new { _anna })).ShouldBe(1);
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM kb_entry WHERE id = @_anna", new { _anna })).ShouldBeTrue();
    }

    [Test, Order(2)]
    public async Task Delete_WhenNothingFails_ShouldRewriteTheSceneAndArchiveTheEntry()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync("DROP TRIGGER refuse_archive ON kb_entry");
        }

        var response = await Client.DeleteAsync($"/api/kb/entries/{_anna}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene}/prose"))!.Body.ShouldBe("Hello Anna.");
        await using var check = await OpenDatabaseAsync();
        (await check.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM kb_entry WHERE id = @_anna", new { _anna })).ShouldBeFalse();
    }
}
