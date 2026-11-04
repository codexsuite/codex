using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Citations;

// J9 A on the seed: cite Accelerate again in Ch 7 Sc 1 (marker 8, needs check), cite the uncited Lean Software
// Development there (later sources' numbers move up), then remove that marker (they move back and the use is archived).
[TestFixture]
public class CitePassageApiTests : ApiFixture
{
    private static readonly Guid Scene = SeedIds.Scenes.Ch7Sc1;
    private Guid _leanUse;

    private async Task<ProseDto> ProseAsync() =>
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose", JsonSerializerOptions.Web))!;

    // The first sentence of the n-th paragraph (the scene's filler paragraphs hold 12-word sentences).
    private static string Sentence(string body, int paragraph)
    {
        var text = body.Split("\n\n")[paragraph];
        return text[..(text.IndexOf('.') + 1)];
    }

    private async Task<CitePassageResult> CiteAsync(Guid sourceId, string passage, int version)
    {
        var response = await Client.PostAsJsonAsync("/api/citations", new CitePassageRequest(Scene, sourceId, version, passage));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CitePassageResult>(JsonSerializerOptions.Web))!;
    }

    private async Task<List<MarkerDto>> MarkersAsync() =>
        (await Client.GetFromJsonAsync<List<MarkerDto>>($"/api/books/{SeedIds.Books.ProductEngineering}/markers", JsonSerializerOptions.Web))!;

    private static int NumberOf(List<MarkerDto> markers, Guid useId) => markers.Single(m => m.UseId == useId).Number;

    [Test, Order(1)]
    public async Task Cite_WhenAccelerateIsCitedInCh7Sc1_ShouldGiveMarker8NeedingCheck()
    {
        var before = await ProseAsync();
        var sentence = Sentence(before.Body, 1);

        var result = await CiteAsync(SeedIds.References.Accelerate, sentence, before.Version);

        result.Number.ShouldBe(8);
        result.MarkersShifted.ShouldBe(0);
        result.StatusBefore.ShouldBe("needs_check");
        result.Status.ShouldBe("needs_check");
        result.Reason.ShouldBe("new_uses_since_signoff");
        result.Version.ShouldBe(before.Version + 1);
        var at = before.Body.IndexOf(sentence, StringComparison.Ordinal);
        result.Body.ShouldBe(before.Body[..at] + $"[{sentence}](ref:{result.UseId})" + before.Body[(at + sentence.Length)..]);
        var after = await ProseAsync();
        after.Body.ShouldBe(result.Body);
        after.Version.ShouldBe(result.Version);
        var markers = await MarkersAsync();
        NumberOf(markers, result.UseId).ShouldBe(8);
        NumberOf(markers, SeedIds.ReferenceUses.TeamTopologiesCh9Sc1).ShouldBe(9);
        await using var db = await OpenDatabaseAsync();
        var row = await db.QuerySingleAsync<(string Kind, string Excerpt, Guid Scene)>(
            "SELECT kind, excerpt, scene_id FROM reference_use WHERE id = @id", new { id = result.UseId });
        row.ShouldBe(("paraphrase", sentence, Scene));
    }

    [Test, Order(2)]
    public async Task Cite_WhenAnUncitedSourceIsCited_ShouldShiftLaterMarkersUpByOne()
    {
        var before = await ProseAsync();

        var result = await CiteAsync(SeedIds.References.LeanSoftware, Sentence(before.Body, 2), before.Version);
        _leanUse = result.UseId;

        result.Number.ShouldBe(9);
        result.StatusBefore.ShouldBe("not_cited");
        result.MarkersShifted.ShouldBe(3);
        var markers = await MarkersAsync();
        NumberOf(markers, SeedIds.ReferenceUses.TeamTopologiesCh9Sc1).ShouldBe(10);
        NumberOf(markers, SeedIds.ReferenceUses.MythicalManMonthCh9Sc1).ShouldBe(11);
        NumberOf(markers, SeedIds.ReferenceUses.ShipItFridayCh10Sc1).ShouldBe(12);
        NumberOf(markers, SeedIds.ReferenceUses.AccelerateCh7Sc1).ShouldBe(8);
    }

    [Test, Order(3)]
    public async Task Remove_ShouldArchiveTheUseAndShiftLaterMarkersBack()
    {
        var before = await ProseAsync();

        var response = await Client.PostAsJsonAsync($"/api/citations/{_leanUse}/remove", new RemoveCitationRequest(before.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<RemoveCitationResult>(JsonSerializerOptions.Web))!;
        result.MarkersShifted.ShouldBe(3);
        result.Status.ShouldBe("not_cited");
        result.Body.ShouldNotContain(_leanUse.ToString());
        result.Body.ShouldContain(Sentence(result.Body, 2));
        (await ProseAsync()).Version.ShouldBe(before.Version + 1);
        var markers = await MarkersAsync();
        markers.ShouldNotContain(m => m.UseId == _leanUse);
        NumberOf(markers, SeedIds.ReferenceUses.TeamTopologiesCh9Sc1).ShouldBe(9);
        NumberOf(markers, SeedIds.ReferenceUses.ShipItFridayCh10Sc1).ShouldBe(11);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>("SELECT archived_at IS NOT NULL FROM reference_use WHERE id = @id", new { id = _leanUse })).ShouldBeTrue();
    }

    [Test, Order(4)]
    public async Task Remove_WhenTheUseIsInAPendingDraft_ShouldBeValidation()
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/citations/{SeedIds.ReferenceUses.AccelerateCh5Sc2}/remove", new RemoveCitationRequest(1));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
    }

    [Test, Order(5)]
    public async Task Remove_WhenTheUseIsArchived_ShouldBeNotFound()
    {
        var response = await Client.PostAsJsonAsync($"/api/citations/{_leanUse}/remove", new RemoveCitationRequest(1));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }
}
