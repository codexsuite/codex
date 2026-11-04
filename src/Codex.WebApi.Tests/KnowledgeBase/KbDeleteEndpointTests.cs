using System.Net;
using System.Net.Http.Json;
using Codex.Contracts.KnowledgeBase;
using Codex.Contracts.Manuscript;
using Dapper;

namespace Codex.WebApi.Tests.KnowledgeBase;

// Delete with Undo, in order, on one cloned schema: mentions, a timeline note, summary, beat, links and another entry's text.
[TestFixture]
public class KbDeleteEndpointTests : BookContentFixture
{
    private Guid _anna;
    private Guid _ben;
    private Guid _scene1;
    private Guid _scene2;
    private Guid _beat;
    private Guid _note;
    private string _body1 = "";
    private KbUndoRecordDto _record = null!;

    [OneTimeSetUp]
    public async Task InsertScenarioAsync()
    {
        var book = await AddBookAsync("Tides");
        var people = await AddCategoryAsync(book, "People");
        _anna = await AddEntryAsync(book, people, "Anna");
        _ben = await AddEntryAsync(book, people, "Ben");
        var chapter = await AddChapterAsync(book);
        _scene1 = await AddSceneAsync(book, chapter, "a0");
        _scene2 = await AddSceneAsync(book, chapter, "a1", "No one here.");
        _beat = await AddBeatAsync(_scene1);
        await using var db = await OpenDatabaseAsync();
        _note = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO kb_progression (kb_entry_id, scene_id, body) VALUES (@anna, @scene, 'Anna is a pilot.') RETURNING id", new { anna = _anna, scene = _scene1 });
        _body1 = $"::beat{{id=\"{_beat}\"}}\n\nHello [Anna](kb:{_anna}) and [Ben](kb:{_ben}).\n\n::kb-addition{{id=\"{_note}\"}}\n\nBye [Anna](kb:{_anna}).";
        await db.ExecuteAsync("UPDATE prose SET body = @body WHERE scene_id = @scene1", new { body = _body1, scene1 = _scene1 });
        await db.ExecuteAsync("UPDATE scene SET summary = @summary WHERE id = @scene2", new { summary = $"About [Anna](kb:{_anna}).", scene2 = _scene2 });
        await db.ExecuteAsync("UPDATE beat SET body = @body WHERE id = @beat", new { body = $"[Anna](kb:{_anna}) enters", beat = _beat });
        await db.ExecuteAsync("UPDATE kb_entry SET body = @body WHERE id = @ben", new { body = $"Friend of [Anna](kb:{_anna}).", ben = _ben });
        await db.ExecuteAsync("INSERT INTO scene_kb VALUES (@scene2, @anna)", new { scene2 = _scene2, anna = _anna });
        await db.ExecuteAsync("INSERT INTO beat_kb VALUES (@beat, @anna)", new { beat = _beat, anna = _anna });
        await db.ExecuteAsync("INSERT INTO kb_entry_link VALUES (least(@anna, @ben), greatest(@anna, @ben))", new { anna = _anna, ben = _ben });
    }

    [Test, Order(1)]
    public async Task Impact_ShouldCountMentionsScenesNotesAndLinks()
    {
        var impact = (await Client.GetFromJsonAsync<KbDeleteImpactDto>($"/api/kb/entries/{_anna}/delete-impact"))!;

        // 2 in the scene, 1 summary, 1 beat, 1 in Ben's content; both scenes; one note; one related link.
        impact.ShouldBe(new KbDeleteImpactDto(5, 2, 1, 1));
    }

    [Test, Order(2)]
    public async Task Restore_WhenNotDeleted_ShouldReturn404()
    {
        var error = await ApiErrors.ReadApiErrorAsync(
            await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/restore", Record(_anna)), HttpStatusCode.NotFound);

        error.Code.ShouldBe("not_found");
    }

    [Test, Order(3)]
    public async Task Delete_ShouldTurnMentionsToPlainTextAndRemoveLinksAndNotes()
    {
        var response = await Client.DeleteAsync($"/api/kb/entries/{_anna}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _record = (await response.Content.ReadFromJsonAsync<KbUndoRecordDto>())!;
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!.Body
            .ShouldBe($"::beat{{id=\"{_beat}\"}}\n\nHello Anna and [Ben](kb:{_ben}).\n\nBye Anna.");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @_scene2", new { _scene2 })).ShouldBe("About Anna.");
        (await db.ExecuteScalarAsync<string>("SELECT body FROM beat WHERE id = @_beat", new { _beat })).ShouldBe("Anna enters");
        (await db.ExecuteScalarAsync<string>("SELECT body FROM kb_entry WHERE id = @_ben", new { _ben })).ShouldBe("Friend of Anna.");
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_progression WHERE id = @_note", new { _note })).ShouldBeTrue();
        // The links are archived, not deleted, and the record names the ones it archived.
        (await db.ExecuteScalarAsync<int>(LinkCount("archived_at IS NULL"))).ShouldBe(0);
        (await db.ExecuteScalarAsync<int>(LinkCount("archived_at IS NOT NULL"))).ShouldBe(3);
        _record.Related.ShouldBe([_ben]);
        _record.SceneLinks.ShouldBe([_scene2]);
        _record.BeatContexts.ShouldBe([_beat]);
        (await Client.GetFromJsonAsync<KbEntryDto>($"/api/kb/entries/{_ben}"))!.Related.ShouldBeEmpty();
        (await Client.GetAsync($"/api/kb/entries/{_anna}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(4)]
    public async Task Delete_WhenAlreadyDeleted_ShouldReturn404()
    {
        (await ApiErrors.ReadApiErrorAsync(await Client.DeleteAsync($"/api/kb/entries/{_anna}"), HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        (await ApiErrors.ReadApiErrorAsync(await Client.GetAsync($"/api/kb/entries/{_anna}/delete-impact"), HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(5)]
    public async Task Restore_ShouldPutEverythingBack()
    {
        var response = await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/restore", _record);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = (await response.Content.ReadFromJsonAsync<KbEntryDto>())!;
        entry.Related.Select(r => r.Name).ShouldBe(["Ben"]);
        entry.Timeline.Select(t => t.Id).ShouldBe([_note]);
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!.Body.ShouldBe(_body1);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT summary FROM scene WHERE id = @_scene2", new { _scene2 })).ShouldBe($"About [Anna](kb:{_anna}).");
        (await db.ExecuteScalarAsync<string>("SELECT body FROM beat WHERE id = @_beat", new { _beat })).ShouldBe($"[Anna](kb:{_anna}) enters");
        (await db.ExecuteScalarAsync<string>("SELECT body FROM kb_entry WHERE id = @_ben", new { _ben })).ShouldBe($"Friend of [Anna](kb:{_anna}).");
        (await db.ExecuteScalarAsync<int>("SELECT (SELECT count(*) FROM scene_kb) + (SELECT count(*) FROM beat_kb)")).ShouldBe(2);
        // Undo revived the archived rows.
        (await db.ExecuteScalarAsync<int>(LinkCount("archived_at IS NULL"))).ShouldBe(3);
        (await db.ExecuteScalarAsync<int>(LinkCount("archived_at IS NOT NULL"))).ShouldBe(0);
        (await Client.GetFromJsonAsync<KbDeleteImpactDto>($"/api/kb/entries/{_anna}/delete-impact"))!.ShouldBe(new KbDeleteImpactDto(5, 2, 1, 1));
    }

    [Test, Order(6)]
    public async Task Restore_WhenASceneChangedSinceTheDelete_ShouldConflictAndStayDeleted()
    {
        _record = (await (await Client.DeleteAsync($"/api/kb/entries/{_anna}")).Content.ReadFromJsonAsync<KbUndoRecordDto>())!;
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!;
        (await Client.PutAsJsonAsync($"/api/scenes/{_scene1}/prose", new SaveProseRequest(prose.Body + "\n\nMore.", prose.Version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var error = await ApiErrors.ReadApiErrorAsync(
            await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/restore", _record), HttpStatusCode.Conflict);

        error.Code.ShouldBe("conflict");
        (await Client.GetAsync($"/api/kb/entries/{_anna}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_scene1}/prose"))!.Body.ShouldEndWith("More.");
    }

    [Test, Order(7)]
    public async Task Restore_WhenRecordIsForAnotherEntry_ShouldReturn400()
    {
        var error = await ApiErrors.ReadApiErrorAsync(
            await Client.PostAsJsonAsync($"/api/kb/entries/{_anna}/restore", Record(_ben)), HttpStatusCode.BadRequest);

        error.Code.ShouldBe("validation");
    }

    private static string LinkCount(string where) =>
        $"SELECT (SELECT count(*) FROM kb_entry_link WHERE {where}) + (SELECT count(*) FROM scene_kb WHERE {where}) + (SELECT count(*) FROM beat_kb WHERE {where})";

    private static KbUndoRecordDto Record(Guid entryId) => new(entryId, "x", [], [], [], [], [], [], []);
}
