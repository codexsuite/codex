using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Ai;

// The seed's pending draft at Ch 5 Sc 2 (3 paragraphs, 3 source markers): listed on load, applied in place.
[TestFixture]
public class SeedDraftTests : ApiFixture
{
    [Test]
    [Order(1)]
    public async Task List_WhenSeeded_ShouldShowThePendingDraftOfTheOutcomeMetricsBeat()
    {
        var drafts = await DraftStreams.GetAsync<BookDraftsDto>(Client, $"/api/books/{SeedIds.Books.ProductEngineering}/drafts");

        var draft = drafts.Drafts.ShouldHaveSingleItem();
        draft.Id.ShouldBe(SeedIds.AiGenerations.Ch5Sc2Pending);
        draft.BeatId.ShouldBe(SeedIds.Beats.Ch5Sc2OutcomeMetrics);
        draft.SceneId.ShouldBe(SeedIds.Scenes.Ch5Sc2);
        draft.Model.ShouldBe("claude-opus-5-5");
        draft.Attempt.ShouldBe(1);
        draft.Template.ShouldBe("General purpose (Nonfiction)");
        draft.Words.ShouldBeInRange(380, 400);
    }

    [Test]
    [Order(2)]
    public async Task Apply_ShouldWriteTheDraftIntoTheSceneAndKeepItsSourceMarkers()
    {
        var prose = (await DraftStreams.GetAsync<ProseDto>(Client, $"/api/scenes/{SeedIds.Scenes.Ch5Sc2}/prose"));
        await using var db = await OpenDatabaseAsync();
        var markers = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL AND scene_id = @id", new { id = SeedIds.Scenes.Ch5Sc2 });

        var response = await Client.PostAsJsonAsync($"/api/generations/{SeedIds.AiGenerations.Ch5Sc2Pending}/apply", new DraftVersionRequest(prose.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await DraftStreams.ReadAsync<DraftApplyResult>(response));
        result.Body.ShouldStartWith($"::beat{{id=\"{SeedIds.Beats.Ch5Sc2OutcomeMetrics}\"}}\n\n[Outcome metrics rot when they become targets](ref:");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL AND scene_id = @id", new { id = SeedIds.Scenes.Ch5Sc2 })).ShouldBe(markers);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE generation_id IS NOT NULL")).ShouldBe(0);
        (await db.ExecuteScalarAsync<Guid>("SELECT generation_id FROM prose_revision WHERE scene_id = @id AND source = 'ai_apply'", new { id = SeedIds.Scenes.Ch5Sc2 }))
            .ShouldBe(SeedIds.AiGenerations.Ch5Sc2Pending);
    }
}

// Discard then Undo must give the seeded draft its three source markers back.
[TestFixture]
public class SeedDraftUndoTests : ApiFixture
{
    [Test]
    public async Task Discard_ThenUndo_ShouldRestoreTheSourceUsesAndLeaveTheReferencesSummaryUnchanged()
    {
        var summaryPath = $"/api/books/{SeedIds.Books.ProductEngineering}/references/summary";
        var before = await Client.GetStringAsync(summaryPath);
        await using var db = await OpenDatabaseAsync();
        const string Live = "SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL AND generation_id = @id";
        var id = SeedIds.AiGenerations.Ch5Sc2Pending;
        (await db.ExecuteScalarAsync<int>(Live, new { id })).ShouldBe(3);

        (await Client.PostAsync($"/api/generations/{id}/discard", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL AND scene_id = @s", new { s = SeedIds.Scenes.Ch5Sc2 })).ShouldBe(0);

        (await Client.PostAsync($"/api/generations/{id}/undo-discard", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await db.ExecuteScalarAsync<int>(Live, new { id })).ShouldBe(3);
        (await Client.GetStringAsync(summaryPath)).ShouldBe(before);
    }

    [Test]
    public async Task Apply_ThenUndo_ShouldLinkTheSourceUsesToTheDraftAgain()
    {
        var id = SeedIds.AiGenerations.Ch5Sc2Pending;
        var prose = await DraftStreams.GetAsync<ProseDto>(Client, $"/api/scenes/{SeedIds.Scenes.Ch5Sc2}/prose");
        var applied = await Client.PostAsJsonAsync($"/api/generations/{id}/apply", new DraftVersionRequest(prose.Version));
        var result = await DraftStreams.ReadAsync<DraftApplyResult>(applied);

        var undone = await Client.PostAsJsonAsync($"/api/generations/{id}/undo-apply", new DraftVersionRequest(result.Version));

        undone.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM reference_use WHERE archived_at IS NULL AND generation_id = @id", new { id })).ShouldBe(3);
    }
}
