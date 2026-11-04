using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

[TestFixture]
public class SourceFairUseTests : SourceApiFixture
{
    [Test, Order(1)]
    public async Task FairUse_WhenReasonIsBlank_ShouldBe400()
    {
        using var response = await Client.PostAsJsonAsync($"/api/references/{SeedIds.References.AgileManifestoWeb}/fair-use", new FairUseRequest("  "));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(2)]
    public async Task FairUse_WhenLicenceNeedsNoDecision_ShouldBe400()
    {
        using var response = await Client.PostAsJsonAsync($"/api/references/{SeedIds.References.BeckXp}/fair-use", new FairUseRequest("Short quote."));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(3)]
    public async Task FairUse_WhenAccepted_ShouldMoveManifestoToReadyToSignOff()
    {
        var before = await GetSourceAsync(SeedIds.References.AgileManifestoWeb);
        before.Verdict.Action.ShouldBe("accept_fair_use");

        var source = await PostSourceAsync(SeedIds.References.AgileManifestoWeb, "fair-use", new FairUseRequest("Seven words, quoted to be discussed."));

        (source.Verdict.Reason, source.Verdict.Action).ShouldBe(("ready_to_sign_off", "sign_off"));
        source.Details.FairUseNote.ShouldBe("Seven words, quoted to be discussed.");
        source.History[0].Text.ShouldStartWith("accepted fair use");
    }

    [Test, Order(4)]
    public async Task SignOff_WhenFairUseDecided_ShouldClearManifesto()
    {
        var source = await PostSourceAsync(SeedIds.References.AgileManifestoWeb, "sign-off");

        source.Verdict.Status.ShouldBe("cleared");
        (await GetSummaryAsync()).NeedsCheck.ShouldBe(3);
    }
}
