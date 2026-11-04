using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Structure;
using Dapper;

namespace Codex.WebApi.Tests.Structure;

// Merge into the previous scene: text appended, summaries joined, labels and links combined, uses and beats moved,
// the absorbed scene archived (J16 step 7).
[TestFixture]
public class SceneMergeTests : SchemaApiFixture
{
    private Guid _bookId;
    private Guid _chapterId;
    private Guid _first;
    private Guid _second;
    private Guid _third;
    private Guid _label;
    private Guid _entry;
    private Guid _useId;
    private Guid _beat;

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        _bookId = await TestRows.BookAsync(db);
        _chapterId = await TestRows.ChapterAsync(db, _bookId, await TestRows.FirstActAsync(db, _bookId), "a0", "One");
        _first = await TestRows.SceneAsync(db, _bookId, _chapterId, "a0", summary: "First.");
        _second = await TestRows.SceneAsync(db, _bookId, _chapterId, "a1", summary: "Second.");
        _third = await TestRows.SceneAsync(db, _bookId, _chapterId, "a2");
        _label = await TestRows.LabelAsync(db, _bookId, "Drafting");
        _entry = await TestRows.EntryAsync(db, _bookId, await TestRows.CategoryAsync(db, _bookId, "People"), "Ada");
        await db.ExecuteAsync("INSERT INTO scene_label (scene_id, label_id) VALUES (@_second, @_label)", new { _second, _label });
        await db.ExecuteAsync("INSERT INTO scene_kb (scene_id, kb_entry_id) VALUES (@_second, @_entry)", new { _second, _entry });
        var source = await TestRows.SourceAsync(db, _bookId, "Manifesto");
        _useId = await TestRows.UseAsync(db, _bookId, source, _second, "quoted");
        _beat = await TestRows.BeatAsync(db, _second);
        await TestRows.ProseAsync(db, _first, "Alpha.\n");
        await TestRows.ProseAsync(db, _second, $"Beta [quoted](ref:{_useId}).\n\n::beat{{id=\"{_beat}\"}}\n");
    }

    [Test, Order(1)]
    public async Task Merge_WhenThereIsNoEarlierScene_400()
    {
        var response = await Client.PostAsync($"/api/scenes/{_first}/merge-previous", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(2)]
    public async Task Merge_ShouldJoinTextAndSummariesAndArchiveTheAbsorbedScene()
    {
        var response = await Client.PostAsync($"/api/scenes/{_second}/merge-previous", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<MergeSceneResult>(JsonSerializerOptions.Web))!;
        (result.SceneId, result.ArchivedSceneId).ShouldBe((_first, _second));
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_first}/prose", JsonSerializerOptions.Web))!;
        prose.Body.ShouldBe($"Alpha.\n\nBeta [quoted](ref:{_useId}).\n\n::beat{{id=\"{_beat}\"}}\n");
        var chapter = (await OutlineAsync()).Acts[0].Chapters.Single();
        chapter.Scenes.Select(s => s.Id).ShouldBe([_first, _third]);
        chapter.Scenes[0].Summary.ShouldBe("First.\n\nSecond.");
        chapter.Scenes[0].Labels.Select(l => l.Id).ShouldBe([_label]);
        chapter.Scenes[0].LinkedEntries.Select(e => e.Id).ShouldBe([_entry]);
        chapter.ArchivedSceneCount.ShouldBe(1);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM reference_use WHERE id = @_useId", new { _useId })).ShouldBe(_first);
        (await db.ExecuteScalarAsync<Guid>("SELECT scene_id FROM beat WHERE id = @_beat", new { _beat })).ShouldBe(_first);
    }

    [Test, Order(3)]
    public async Task Merge_WhenTheSceneIsArchived_404()
    {
        var response = await Client.PostAsync($"/api/scenes/{_second}/merge-previous", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(4)]
    public async Task Merge_WhenAPendingDraftExists_422AndNothingChanges()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync(
            "INSERT INTO ai_generation (book_id, scene_id, kind, model, request, status) VALUES (@_bookId, @_first, 'continue', 'm', '{}', 'pending')",
            new { _bookId, _first });
        var before = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_first}/prose", JsonSerializerOptions.Web))!;

        var response = await Client.PostAsync($"/api/scenes/{_third}/merge-previous", null);

        (await response.ReadApiErrorAsync((HttpStatusCode)422)).Code.ShouldBe(ApiErrorCodes.Blocked);
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{_first}/prose", JsonSerializerOptions.Web))!.Version.ShouldBe(before.Version);
        (await OutlineAsync()).Acts[0].Chapters.Single().Scenes.Count.ShouldBe(2);
    }

    private async Task<OutlineDto> OutlineAsync()
        => (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{_bookId}/outline", JsonSerializerOptions.Web))!;
}
