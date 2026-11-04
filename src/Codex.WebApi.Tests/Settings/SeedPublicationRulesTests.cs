using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Settings;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.Settings;

// The seeded Product Engineering book: Velocity Trap quotes 342 words against a 300 limit, three sources are at risk.
[TestFixture]
public class SeedPublicationRulesTests : ApiFixture
{
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();

    private async Task<RulesImpactDto> ImpactAsync(string query = "") =>
        (await Client.GetFromJsonAsync<RulesImpactDto>($"/api/books/{Book}/settings/rules-impact{query}"))!;

    private async Task<ClearanceSummaryDto> SummaryAsync() =>
        (await Client.GetFromJsonAsync<ClearanceSummaryDto>($"/api/books/{Book}/references/summary"))!;

    [Test, Order(1)]
    public async Task Impact_WhenSeeded_ShouldAgreeWithTheClearanceSummary()
    {
        var impact = await ImpactAsync();
        var summary = await SummaryAsync();

        impact.Limit.ShouldBe(300);
        impact.RiskCount.ShouldBe(summary.Risks);
        impact.Sources.ShouldContain(s => s.Title.Contains("Velocity") && s.QuotedWords == 342 && s.AtRisk && s.Reason == "quote_over_limit");
    }

    [Test, Order(2)]
    public async Task Impact_WhenLimitRaised_ShouldDropTheVelocityTrapRiskWithoutSavingAnything()
    {
        var impact = await ImpactAsync("?limit=400");

        impact.Sources.ShouldContain(s => s.Title.Contains("Velocity") && !s.AtRisk);
        impact.RiskCount.ShouldBe((await SummaryAsync()).Risks - 1);
        (await SummaryAsync()).Risks.ShouldBe(3);
    }

    [Test, Order(3)]
    public async Task Patch_WhenLimitLowered_ShouldRaiseTheSummaryRisksByTheImpactStripsCount()
    {
        var predicted = await ImpactAsync("?limit=15");

        var response = await Client.PatchAsJsonAsync($"/api/books/{Book}/publication-settings",
            new PatchPublicationSettingsRequest(null, 15, null, null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await SummaryAsync();
        summary.Risks.ShouldBeGreaterThan(3);
        summary.Risks.ShouldBe(predicted.RiskCount);
    }

    [Test, Order(4)]
    public async Task Impact_WhenLimitIsZero_ShouldAnswer400()
    {
        var response = await Client.GetAsync($"/api/books/{Book}/settings/rules-impact?limit=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadApiErrorAsync()).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(5)]
    public async Task Layout_WhenBookHasNoTemplate_ShouldAnswerNullTemplateAndTheRiskCount()
    {
        var layout = (await Client.GetFromJsonAsync<LayoutSettingsDto>($"/api/books/{Book}/settings/layout"))!;

        layout.Template.ShouldBeNull();
        layout.RiskCount.ShouldBe((await SummaryAsync()).Risks);
    }
}
