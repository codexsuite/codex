using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Codex.WebApi.Tests.Ai;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

// J15 on the seed's Ch 5 Sc 2: apply the pending draft, type a sentence, then walk the history with Restore.
[TestFixture]
public class ProseHistoryTests : ApiFixture
{
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc2;
    private static string HistoryPath => $"/api/scenes/{Scene}/prose/revisions";

    private string _applied = "";
    private int _appliedWords;
    private Guid _editedRevision;

    private Task<ProseHistoryDto> HistoryAsync() => DraftStreams.GetAsync<ProseHistoryDto>(Client, HistoryPath);

    private async Task<RestoreRevisionResult> RestoreAsync(Guid revisionId)
    {
        var response = await Client.PostAsync($"{HistoryPath}/{revisionId}/restore", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await DraftStreams.ReadAsync<RestoreRevisionResult>(response);
    }

    private async Task<int> LiveUsesAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL AND scene_id = @Scene", new { Scene });
    }

    [Test]
    [Order(1)]
    public async Task List_WhenSeeded_ShouldShowTheImportAsTheCurrentVersion()
    {
        var history = await HistoryAsync();

        history.Archived.ShouldBeFalse();
        var row = history.Revisions.ShouldHaveSingleItem();
        (row.Source, row.Label, row.Words, row.Current).ShouldBe(("import", "Imported", 0, true));
    }

    [Test]
    [Order(2)]
    public async Task Apply_ThenType_ShouldListEditedAboveAiDraftAppliedAboveImported()
    {
        var prose = await DraftStreams.GetAsync<ProseDto>(Client, $"/api/scenes/{Scene}/prose");
        var apply = await Client.PostAsJsonAsync($"/api/generations/{SeedIds.AiGenerations.Ch5Sc2Pending}/apply", new DraftVersionRequest(prose.Version));
        var result = await DraftStreams.ReadAsync<DraftApplyResult>(apply);
        _applied = result.Body;
        var put = await Client.PutAsJsonAsync($"/api/scenes/{Scene}/prose", new SaveProseRequest(_applied + "\n\nWe measured outcomes, not output.", result.Version));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var history = await HistoryAsync();

        history.Revisions.Select(r => r.Label).ShouldBe(["Edited", "AI draft applied", "Imported"]);
        history.Revisions.Select(r => r.Current).ShouldBe([true, false, false]);
        history.Revisions[1].Detail.ShouldBe("Opus 5.5 · attempt 1");
        _appliedWords = history.Revisions[1].Words;
        history.Revisions[0].Words.ShouldBe(_appliedWords + 5);
        history.CurrentWords.ShouldBe(_appliedWords + 5);
        _editedRevision = history.Revisions[0].Id;
    }

    [Test]
    [Order(3)]
    public async Task Put_WhenSavedAgainWithinTenMinutes_ShouldUpdateTheEditedRevisionInPlace()
    {
        var version = (await DraftStreams.GetAsync<ProseDto>(Client, $"/api/scenes/{Scene}/prose")).Version;
        await Client.PutAsJsonAsync($"/api/scenes/{Scene}/prose", new SaveProseRequest(_applied + "\n\nWe measured outcomes, not output!", version));

        var history = await HistoryAsync();

        history.Revisions.Count.ShouldBe(3);
        history.Revisions[0].Id.ShouldBe(_editedRevision);
        history.Revisions[0].Body.ShouldEndWith("output!");
    }

    [Test]
    [Order(4)]
    public async Task Restore_ShouldPutTheVersionBackKeepTheReplacedTextAndCutARestoreRevision()
    {
        var applied = (await HistoryAsync()).Revisions.Single(r => r.Source == "ai_apply");

        var result = await RestoreAsync(applied.Id);

        result.Body.ShouldBe(_applied);
        result.PreviousRevisionId.ShouldBe(_editedRevision);
        var history = await HistoryAsync();
        history.Revisions.Select(r => r.Label).ShouldBe(["Restored", "Edited", "AI draft applied", "Imported"]);
        history.Revisions[0].Current.ShouldBeTrue();
        history.CurrentWords.ShouldBe(_appliedWords);
    }

    [Test]
    [Order(5)]
    public async Task Restore_WhenUndone_ShouldBringTheEditBackAsAnotherRestore()
    {
        var result = await RestoreAsync(_editedRevision);

        result.Body.ShouldEndWith("output!");
        var history = await HistoryAsync();
        history.Revisions.Count.ShouldBe(5);
        history.Revisions[0].Source.ShouldBe("restore");
    }

    [Test]
    [Order(6)]
    public async Task Restore_WhenImportedThenDraftVersion_ShouldArchiveThenReturnTheSourceUses()
    {
        var before = await LiveUsesAsync();
        before.ShouldBe(3);
        var history = await HistoryAsync();

        await RestoreAsync(history.Revisions.Single(r => r.Source == "import").Id);
        (await LiveUsesAsync()).ShouldBe(0);
        await using (var db = await OpenDatabaseAsync())
        {
            (await db.ExecuteScalarAsync<string>("SELECT status FROM ai_generation WHERE id = @id", new { id = SeedIds.AiGenerations.Ch5Sc2Pending })).ShouldBe("applied");
        }

        await RestoreAsync((await HistoryAsync()).Revisions.First(r => r.Source == "ai_apply").Id);
        (await LiveUsesAsync()).ShouldBe(3);
    }

    [Test]
    [Order(7)]
    public async Task Restore_WhenTheCurrentVersion_ShouldBe400()
    {
        var current = (await HistoryAsync()).Revisions.First(r => r.Current);

        var response = await Client.PostAsync($"{HistoryPath}/{current.Id}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(8)]
    public async Task Restore_WhenTheRevisionBelongsToAnotherScene_ShouldBe404()
    {
        var other = Guid.NewGuid();

        var response = await Client.PostAsync($"{HistoryPath}/{other}/restore", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(9)]
    public async Task Restore_WhenTheSceneIsArchived_ShouldBeBlockedAndTheHistoryReadOnly()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE scene SET archived_at = now() WHERE id = @Scene", new { Scene });
        var history = await HistoryAsync();

        var response = await Client.PostAsync($"{HistoryPath}/{history.Revisions[^1].Id}/restore", null);

        history.Archived.ShouldBeTrue();
        (await response.ReadApiErrorAsync(HttpStatusCode.UnprocessableEntity)).Code.ShouldBe(ApiErrorCodes.Blocked);
    }
}
