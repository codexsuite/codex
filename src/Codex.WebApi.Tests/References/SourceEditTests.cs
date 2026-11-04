using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Core;

namespace Codex.WebApi.Tests.References;

// Source details, the Codex link and use types, edited in place on Velocity Trap and Beck.
[TestFixture]
public class SourceEditTests : SourceApiFixture
{
    private static SourceDetailsRequest FromDetails(SourceDetailsDto d) =>
        new(d.Kind, d.Title, d.AuthorsText, d.Year, d.Publisher, d.Edition, d.Isbn, d.Doi, d.Url, d.AccessedOn, d.Licence, d.RightsHolder);

    private static DateOnly Today() => DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone);

    private async Task<HttpResponseMessage> PatchAsync(Guid id, PatchSourceRequest request) =>
        await Client.PatchAsJsonAsync($"/api/references/{id}", request);

    [Test, Order(1)]
    public async Task Patch_WhenDetailsChange_ShouldSaveThemAndRecordTheFields()
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);

        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(
            FromDetails(before.Details) with { Publisher = " Fresh Press ", Year = 2022, AuthorsText = "Whitlock, Dana; Ng, Ari; Standards Body" }, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        (source.Publisher, source.Year, source.AuthorsLabel).ShouldBe(("Fresh Press", 2022, "Whitlock et al."));
        source.Details.AuthorsText.ShouldBe("Whitlock, Dana; Ng, Ari; Standards Body");
        source.History[0].Text.ShouldBe("edited authors, year, publisher");
    }

    [Test, Order(2)]
    public async Task Patch_WhenARequiredFieldIsEmptied_ShouldWarnTheCitationIsIncomplete()
    {
        var before = await GetSourceAsync(SeedIds.References.BeckXp);

        using var response = await PatchAsync(SeedIds.References.BeckXp, new PatchSourceRequest(FromDetails(before.Details) with { Publisher = "" }, null));

        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        source.Details.Missing.ShouldBe(["publisher"]);
        (source.Verdict.Status, source.Verdict.Reason, source.Verdict.Action).ShouldBe(("needs_check", "citation_incomplete", "fill_details"));
        source.Checks.Single(c => c.Key == "citation").Detail.ShouldBe("Missing: publisher.");
    }

    [Test, Order(3)]
    public async Task Patch_WhenLinkedToAnEntryInScope_ShouldShowTheEntry()
    {
        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(null, new SourceEntryRequest(SeedIds.KbEntries.FeatureFactory)));

        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        source.Entry.ShouldNotBeNull().Id.ShouldBe(SeedIds.KbEntries.FeatureFactory);
        source.History[0].Text.ShouldStartWith("linked the Codex entry");
    }

    [Test, Order(4)]
    public async Task Patch_WhenTheEntryLinkIsRemoved_ShouldClearIt()
    {
        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(null, new SourceEntryRequest(null)));

        (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!.Entry.ShouldBeNull();
    }

    [Test, Order(5)]
    public async Task Patch_WhenTheEntryIsOutOfScope_ShouldBe400()
    {
        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(null, new SourceEntryRequest(SeedIds.KbEntries.Gravetouch)));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await GetSourceAsync(SeedIds.References.VelocityTrap)).Entry.ShouldBeNull();
    }

    [Test, Order(6)]
    public async Task Patch_WhenTheLicenceIsUnknownToTheSchema_ShouldBe400()
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);

        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(FromDetails(before.Details) with { Licence = "free_for_all" }, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(7)]
    public async Task Patch_WhenTheAccessedDateIsNotADate_ShouldBe400()
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);

        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(FromDetails(before.Details) with { AccessedOn = "yesterday" }, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(7)]
    [TestCase("30/10/2026")]
    [TestCase("2026-10-30T00:00")]
    [TestCase("2026-10-30+07:00")]
    [TestCase("2026-02-30")]
    [TestCase(" 2026-10-19")]
    [TestCase("2026-10-19 ")]
    [TestCase("1899-12-31")]
    [TestCase("0001-01-01")]
    [TestCase("9999-12-31")]
    public async Task Patch_WhenTheAccessedDateIsMalformedOrOutOfRange_ShouldBe400AndChangeNothing(string accessedOn)
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);

        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(FromDetails(before.Details) with { AccessedOn = accessedOn }, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await GetSourceAsync(SeedIds.References.VelocityTrap)).Details.AccessedOn.ShouldBe(before.Details.AccessedOn);
    }

    [Test, Order(7)]
    public async Task Patch_WhenTheAccessedDateIsTomorrow_ShouldBe400()
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);
        var tomorrow = Today().AddDays(1).ToString("yyyy-MM-dd");

        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(FromDetails(before.Details) with { AccessedOn = tomorrow }, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(10)]
    public async Task Patch_WhenTheAccessedDateIsToday_ShouldSaveIt()
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);
        var today = Today().ToString("yyyy-MM-dd");

        using var response = await PatchAsync(SeedIds.References.VelocityTrap, new PatchSourceRequest(FromDetails(before.Details) with { AccessedOn = today }, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!.Details.AccessedOn.ShouldBe(today);
    }

    [Test, Order(8)]
    public async Task PatchUse_WhenAQuoteBecomesAParaphrase_ShouldDropItsWordsFromTheTotalAndLiftTheRisk()
    {
        var before = await GetSourceAsync(SeedIds.References.VelocityTrap);
        before.Verdict.Status.ShouldBe("risk");

        using var response = await Client.PatchAsJsonAsync($"/api/reference-uses/{SeedIds.ReferenceUses.VelocityTrapCh4Sc1}", new PatchUseRequest("paraphrase"));

        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        source.QuotedWords.ShouldBeLessThan(before.QuotedWords);
        source.Uses.Single(u => u.Id == SeedIds.ReferenceUses.VelocityTrapCh4Sc1).Kind.ShouldBe("paraphrase");
        source.Verdict.Status.ShouldBe("needs_check");
        source.History[0].Text.ShouldBe("changed a passage from quote to paraphrase");
    }

    [Test, Order(9)]
    public async Task PatchUse_WhenASignedOffSourceIsRetyped_ShouldReopenIt()
    {
        var beck = await GetSourceAsync(SeedIds.References.CaganEmpowered);
        beck.Verdict.Status.ShouldBe("cleared");

        using var response = await Client.PatchAsJsonAsync($"/api/reference-uses/{SeedIds.ReferenceUses.CaganEmpoweredCh5Sc1}", new PatchUseRequest("data"));

        var source = (await response.Content.ReadFromJsonAsync<SourceDetailDto>())!;
        (source.Verdict.Status, source.Verdict.Reason).ShouldBe(("needs_check", "new_uses_since_signoff"));
        source.Uses.Single().NewSinceSignOff.ShouldBeTrue();
    }
}
