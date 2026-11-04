using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// J24 steps 4 and 6: editing a cited field reopens a sign-off; retyping a use moves the quoted words.
[TestFixture]
public class SourceReopenOnEditTests : SourceApiFixture
{
    private static SourceDetailsRequest FromDetails(SourceDetailsDto d) =>
        new(d.Kind, d.Title, d.AuthorsText, d.Year, d.Publisher, d.Edition, d.Isbn, d.Doi, d.Url, d.AccessedOn, d.Licence, d.RightsHolder);

    [Test, Order(1)]
    public async Task Patch_WhenACitedFieldOfAClearedSourceChanges_ShouldReopenItAndDropTheClearedCount()
    {
        var before = await GetSourceAsync(SeedIds.References.ConwayCommittees);
        before.Verdict.Status.ShouldBe("cleared");
        var cleared = (await GetSummaryAsync()).Cleared;

        using var response = await Client.PatchAsJsonAsync(
            $"/api/references/{SeedIds.References.ConwayCommittees}",
            new PatchSourceRequest(FromDetails(before.Details) with { Year = 1967 }, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        source.Verdict.Status.ShouldBe("needs_check");
        source.History.Select(h => h.Text).ShouldContain("edited year");
        source.History.Select(h => h.Text).ShouldContain("reopened the source: details changed since sign-off");
        (await GetSummaryAsync()).Cleared.ShouldBe(cleared - 1);
    }

    [Test, Order(2)]
    public async Task Patch_WhenOnlyTheLicenceChangesOnAClearedSource_ShouldKeepItCleared()
    {
        var before = await GetSourceAsync(SeedIds.References.CaganEmpowered);
        before.Verdict.Status.ShouldBe("cleared");

        using var response = await Client.PatchAsJsonAsync(
            $"/api/references/{SeedIds.References.CaganEmpowered}",
            new PatchSourceRequest(FromDetails(before.Details) with { RightsHolder = "Wiley" }, null));

        (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!.Verdict.Status.ShouldBe("cleared");
    }

    [Test, Order(3)]
    public async Task PatchUse_WhenAParaphraseBecomesAQuoteAndBack_ShouldCountItsWordsThenDropThemAndStayNeedsCheck()
    {
        var beck = await GetSourceAsync(SeedIds.References.BeckXp);
        var use = beck.Uses.Single();
        use.Kind.ShouldBe("paraphrase");
        beck.QuotedWords.ShouldBe(0);

        using var asQuote = await Client.PatchAsJsonAsync($"/api/reference-uses/{use.Id}", new PatchUseRequest("quote"));
        var quoted = (await asQuote.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        quoted.QuotedWords.ShouldBe(use.Words);
        quoted.QuotedWords.ShouldBeGreaterThan(0);
        (quoted.Verdict.Status, quoted.Verdict.Reason).ShouldBe(("needs_check", "new_uses_since_signoff"));

        using var back = await Client.PatchAsJsonAsync($"/api/reference-uses/{use.Id}", new PatchUseRequest("paraphrase"));
        var restored = (await back.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        restored.QuotedWords.ShouldBe(0);
        restored.Verdict.Status.ShouldBe("needs_check");
    }

    [Test, Order(4)]
    public async Task PatchUse_WhenTheKindIsUnknown_ShouldBe400()
    {
        using var response = await Client.PatchAsJsonAsync($"/api/reference-uses/{SeedIds.ReferenceUses.BeckXpCh3Sc1}", new PatchUseRequest("poem"));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }
}
