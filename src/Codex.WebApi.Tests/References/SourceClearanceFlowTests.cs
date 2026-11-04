using Codex.Contracts;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// Accelerate has one use newer than its sign-off: signing off clears it, reopening puts it back.
[TestFixture]
public class SourceClearanceFlowTests : SourceApiFixture
{
    [Test, Order(1)]
    public async Task SignOff_WhenNewUseSinceSignOff_ShouldClearTheSourceAndRecordWhoAndWhen()
    {
        var before = await GetSummaryAsync();

        var source = await PostSourceAsync(SeedIds.References.Accelerate, "sign-off");

        (source.Verdict.Status, source.Verdict.Reason, source.Verdict.Action).ShouldBe(("cleared", "signed_off", "reopen"));
        source.Uses.ShouldAllBe(u => !u.NewSinceSignOff);
        source.History[0].Text.ShouldBe("marked the source as cleared");
        source.History[0].Who.ShouldBe("Joel Dickson");
        (await GetSummaryAsync()).Cleared.ShouldBe(before.Cleared + 1);
    }

    [Test, Order(2)]
    public async Task Reopen_WhenCleared_ShouldReturnToReadyToSignOff()
    {
        var source = await PostSourceAsync(SeedIds.References.Accelerate, "reopen");

        (source.Verdict.Status, source.Verdict.Reason, source.Verdict.SignedOffLabel).ShouldBe(("needs_check", "ready_to_sign_off", null));
        source.History.Select(h => h.Text).ShouldContain("reopened the source");
    }

    [Test, Order(3)]
    public async Task Reopen_WhenUnknownSource_ShouldBe404()
    {
        using var response = await Client.PostAsync($"/api/references/{Guid.NewGuid()}/reopen", null);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
