using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// A delete whose second scene write is refused by the schema must leave every scene, the summaries and the entry as they were.
[TestFixture]
public class KbDeleteAtomicityTests : BookContentFixture
{
    private Guid _anna;
    private Guid _scene1;
    private Guid _scene2;
    private Guid _otherBookScene;
    private string _body1 = "";
    private string _body2 = "";

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        var book = await AddBookAsync("Tides");
        var people = await AddCategoryAsync(book, "People");
        _anna = await AddEntryAsync(book, people, "Anna");
        var chapter = await AddChapterAsync(book);
        _scene1 = await AddSceneAsync(book, chapter, "a0");
        _scene2 = await AddSceneAsync(book, chapter, "a1");
        var other = await AddBookAsync("Elsewhere");
        _otherBookScene = await AddSceneAsync(other, await AddChapterAsync(other), "a0", "Untouched.");
        _body1 = $"Hello [Anna](kb:{_anna}).";
        _body2 = $"Poison [Anna](kb:{_anna}).";
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE prose SET body = @body WHERE scene_id = @scene", new { body = _body1, scene = _scene1 });
        await db.ExecuteAsync("UPDATE prose SET body = @body WHERE scene_id = @scene", new { body = _body2, scene = _scene2 });
        await db.ExecuteAsync("UPDATE scene SET summary = @summary WHERE id = @scene", new { summary = $"About [Anna](kb:{_anna}).", scene = _scene1 });
        await db.ExecuteAsync(
            """
            CREATE FUNCTION refuse_poison() RETURNS trigger LANGUAGE plpgsql AS
            $$ BEGIN IF NEW.body LIKE 'Poison Anna%' THEN RAISE EXCEPTION 'refused'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER refuse_poison BEFORE UPDATE ON prose FOR EACH ROW EXECUTE FUNCTION refuse_poison();
            """);
    }

    [Test, Order(1)]
    public async Task Delete_WhenTheNthSceneWriteFails_ShouldLeaveEverythingUnchanged()
    {
        var response = await Client.DeleteAsync($"/api/kb/entries/{_anna}");

        (await ApiErrors.ReadApiErrorAsync(response, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!.Body.ShouldBe(_body1);
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene2}/prose"))!.Body.ShouldBe(_body2);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @_scene1", new { _scene1 })).ShouldBe($"About [Anna](kb:{_anna}).");
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM kb_entry WHERE id = @_anna", new { _anna })).ShouldBeTrue();
    }

    [Test, Order(2)]
    public async Task Restore_WhenTheRecordNamesAnotherBooksScene_ShouldReturn400AndWriteNothing()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync("DROP TRIGGER refuse_poison ON prose");
        }
        var record = (await (await Client.DeleteAsync($"/api/kb/entries/{_anna}")).Content.ReadFromJsonAsync<KbUndoRecordDto>())!;
        var foreign = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_otherBookScene}/prose"))!;
        var forged = record with { Scenes = [.. record.Scenes, new KbUndoSceneDto(_otherBookScene, foreign.Version, "Overwritten.")] };

        var response = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/restore", forged);

        (await ApiErrors.ReadApiErrorAsync(response, HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_otherBookScene}/prose"))!.Body.ShouldBe("Untouched.");
        (await Client.GetAsync($"/api/kb/entries/{_anna}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(3)]
    public async Task Restore_WhenARecordBodyIsNotWhatTheDeleteLeft_ShouldConflict()
    {
        var record = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!;
        var forged = new KbUndoRecordDto(_anna, "Anna", [new KbUndoSceneDto(_scene1, record.Version, "Forged text.")], [], [], [], [], [], []);

        var response = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/restore", forged);

        (await ApiErrors.ReadApiErrorAsync(response, HttpStatusCode.Conflict)).Code.ShouldBe("conflict");
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!.Body.ShouldBe("Hello Anna.");
    }
}
