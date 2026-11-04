using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fakes;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Ai;

// One beat: generate, retry, apply (stale, twice), undo-apply, discard and undo-discard.
[TestFixture]
public class DraftFlowTests : SchemaApiFixture
{
    private static readonly FakeProseGenerator Generator = new(["First paragraph. ", "Second sentence here.\n\n", "Another paragraph."]);

    private Guid _bookId;
    private Guid _sceneId;
    private Guid _beatId;
    private Guid _nextBeatId;
    private Guid _oldUse;
    private Guid _entryId;
    private Guid _addition;
    private string _oldBody = "";
    private Guid _first;
    private Guid _second;
    private const string Before = "Opening line.\n";
    private const string Next = "Next beat prose.\n";

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    [OneTimeSetUp]
    public async Task InsertRowsAsync()
    {
        await using var db = await OpenDatabaseAsync();
        (_bookId, _, _sceneId) = await TestRows.SingleSceneAsync(db);
        _beatId = await TestRows.BeatAsync(db, _sceneId);
        _nextBeatId = await TestRows.BeatAsync(db, _sceneId);
        var source = await TestRows.SourceAsync(db, _bookId, "Old source");
        _oldUse = await TestRows.UseAsync(db, _bookId, source, _sceneId, "old words");
        _entryId = await TestRows.EntryAsync(db, _bookId, await TestRows.CategoryAsync(db, _bookId, "People"), "Marty");
        _addition = await db.QuerySingleAsync<Guid>("INSERT INTO kb_progression (kb_entry_id, scene_id, body) VALUES (@_entryId, @_sceneId, 'Knows more.') RETURNING id", new { _entryId, _sceneId });
        await db.ExecuteAsync("INSERT INTO scene_kb (scene_id, kb_entry_id) VALUES (@_sceneId, @_entryId)", new { _sceneId, _entryId });
        _oldBody = $"{Before}\n::beat{{id=\"{_beatId}\"}}\n\nAfter [the old words](ref:{_oldUse}) beat with [Marty](kb:{_entryId}).\n\n::kb-addition{{id=\"{_addition}\"}}\n\nSecond old paragraph.\n\n::beat{{id=\"{_nextBeatId}\"}}\n\n{Next}";
        await TestRows.ProseAsync(db, _sceneId, _oldBody);
    }

    [Test]
    [Order(1)]
    public async Task Generate_ShouldStreamStartedTextAndFinalThenLeaveAPendingDraft()
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest("paraphrase", "claude-sonnet-5-5", 200, null));
        var events = await DraftStreams.ReadAllAsync(response);

        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-ndjson");
        events.Count.ShouldBe(5);
        var started = events[0].Started!;
        started.Attempt.ShouldBe(1);
        started.TargetWords.ShouldBe(200);
        events.Skip(1).Take(3).Select(e => e.Text).ShouldBe(["First paragraph. ", "Second sentence here.\n\n", "Another paragraph."]);
        events[4].Final!.GenerationId.ShouldBe(started.GenerationId);
        events[4].Final!.Words.ShouldBe(7);
        _first = started.GenerationId;

        var drafts = await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{_bookId}/drafts");
        var draft = drafts.Drafts.ShouldHaveSingleItem();
        draft.ShouldBeEquivalentTo(draft with { Id = _first, BeatId = _beatId, Status = "pending", Attempt = 1, Template = "Paraphrase", TemplateId = "paraphrase" });
        draft.Text.ShouldBe("First paragraph. Second sentence here.\n\nAnother paragraph.");
        Generator.Requests[^1].Prompt.ShouldContain("Opening line.");
        Generator.Requests[^1].Prompt.ShouldNotContain("After the beat.");
    }

    [Test]
    [Order(2)]
    public async Task Retry_ShouldCountTheAttemptAndDiscardTheOldDraft()
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, "claude-opus-5-5", null, _first));
        var events = await DraftStreams.ReadAllAsync(response);

        events[0].Started!.Attempt.ShouldBe(2);
        _second = events[0].Started!.GenerationId;
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @_first", new { _first })).ShouldBe("discarded");
        (await db.ExecuteScalarAsync<Guid>("SELECT retry_of_id FROM ai_generation WHERE id = @_second", new { _second })).ShouldBe(_first);
        var drafts = await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{_bookId}/drafts");
        drafts.Drafts.ShouldHaveSingleItem().Id.ShouldBe(_second);
    }

    [Test]
    [Order(3)]
    public async Task Apply_WhenVersionIsStale_ShouldReturn409AndChangeNothing()
    {
        var response = await Client.PostAsJsonAsync($"/api/generations/{_second}/apply", new DraftVersionRequest(9));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @_second", new { _second })).ShouldBe("pending");
    }

    [Test]
    [Order(4)]
    public async Task Apply_ShouldReplaceTheBeatsProseAndCutAnAiApplyRevision()
    {
        var response = await Client.PostAsJsonAsync($"/api/generations/{_second}/apply", new DraftVersionRequest(1));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await DraftStreams.ReadAsync<DraftApplyResult>(response));
        result.Version.ShouldBe(2);
        result.Body.ShouldBe($"{Before}\n::beat{{id=\"{_beatId}\"}}\n\nFirst paragraph. Second sentence here.\n\nAnother paragraph.\n\n::beat{{id=\"{_nextBeatId}\"}}\n\n{Next}");
        await using var db = await OpenDatabaseAsync();
        (await db.QuerySingleAsync<(string Source, Guid? GenerationId, string Body)>(
            "SELECT source, generation_id, body FROM prose_revision WHERE scene_id = @_sceneId AND source = 'ai_apply'", new { _sceneId }))
            .ShouldBe(("ai_apply", _second, result.Body));
        (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @_second", new { _second })).ShouldBe("applied");
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference_use WHERE id = @_oldUse", new { _oldUse })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM kb_progression WHERE id = @_addition", new { _addition })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM prose_revision old JOIN prose_revision applied ON applied.scene_id = old.scene_id AND applied.source = 'ai_apply' WHERE old.scene_id = @_sceneId AND old.body = @_oldBody AND old.created_at < applied.created_at",
            new { _sceneId, _oldBody })).ShouldBe(1);
        (await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{_bookId}/drafts")).Drafts.ShouldBeEmpty();
    }

    [Test]
    [Order(5)]
    public async Task Apply_WhenAppliedTwice_ShouldReturn409()
    {
        var response = await Client.PostAsJsonAsync($"/api/generations/{_second}/apply", new DraftVersionRequest(2));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test]
    [Order(6)]
    public async Task UndoApply_ShouldPutTheOldBodyBackAndMakeTheDraftPendingAgain()
    {
        var stale = await Client.PostAsJsonAsync($"/api/generations/{_second}/undo-apply", new DraftVersionRequest(1));
        (await stale.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);

        var response = await Client.PostAsJsonAsync($"/api/generations/{_second}/undo-apply", new DraftVersionRequest(2));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await DraftStreams.ReadAsync<DraftApplyResult>(response)).Version.ShouldBe(3);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @_sceneId", new { _sceneId }))
            .ShouldBe(_oldBody);
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM reference_use WHERE id = @_oldUse", new { _oldUse })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NULL FROM kb_progression WHERE id = @_addition", new { _addition })).ShouldBeTrue();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene_kb WHERE scene_id = @_sceneId AND kb_entry_id = @_entryId", new { _sceneId, _entryId })).ShouldBe(1);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM prose_revision WHERE scene_id = @_sceneId AND source = 'restore'", new { _sceneId })).ShouldBe(1);
        (await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{_bookId}/drafts")).Drafts.ShouldHaveSingleItem().Id.ShouldBe(_second);
    }

    [Test]
    [Order(7)]
    public async Task Discard_ThenUndo_ShouldHideAndRestoreTheDraft()
    {
        var discarded = await Client.PostAsync($"/api/generations/{_second}/discard", null);
        (await DraftStreams.ReadAsync<DraftDto>(discarded)).Status.ShouldBe("discarded");
        (await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{_bookId}/drafts")).Drafts.ShouldBeEmpty();

        var again = await Client.PostAsync($"/api/generations/{_second}/discard", null);
        (await again.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);

        var restored = await Client.PostAsync($"/api/generations/{_second}/undo-discard", null);
        (await DraftStreams.ReadAsync<DraftDto>(restored)).Status.ShouldBe("pending");
    }

    [Test]
    [Order(8)]
    public async Task Generate_WhenModelIsTooLong_ShouldReturn400()
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{_beatId}/generate", new GenerateDraftRequest(null, new string('m', 201), null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(9)]
    public async Task Generate_WhenBeatIsUnknown_ShouldReturn404()
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/beats/{Guid.NewGuid()}/generate", new GenerateDraftRequest(null, "claude-opus-5-5", null, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(10)]
    public async Task Continue_ThenApply_ShouldAppendToTheEndOfTheScene()
    {
        using var response = await DraftStreams.OpenAsync(Client, $"/api/scenes/{_sceneId}/continue", new GenerateDraftRequest("continue-writing", "claude-sonnet-5-5", null, null));
        var started = (await DraftStreams.ReadAllAsync(response))[0].Started!;

        var applied = await Client.PostAsJsonAsync($"/api/generations/{started.GenerationId}/apply", new DraftVersionRequest(3));

        var body = (await DraftStreams.ReadAsync<DraftApplyResult>(applied)).Body;
        body.ShouldEndWith("Next beat prose.\n\nFirst paragraph. Second sentence here.\n\nAnother paragraph.\n");
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<string>("SELECT kind FROM ai_generation WHERE id = @id", new { id = started.GenerationId })).ShouldBe("continue");
    }
}
