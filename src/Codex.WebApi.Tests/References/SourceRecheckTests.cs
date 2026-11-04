using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.References;

// DORA's address redirects: a re-check over the fake lookup reports it, accepting it updates the field.
[TestFixture]
public class SourceRecheckTests : SourceApiFixture
{
    private const string Redirect = "https://dora.dev/research/2024/dora-report/";

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    [Test, Order(1)]
    public async Task Recheck_WhenTheUrlRedirects_ShouldReportItAndKeepTheWarning()
    {
        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.DoraReport2024}/recheck", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<RecheckResultDto>())!;
        result.Fields.ShouldBe([new RecheckFieldDto("url", "https://dora.dev/research/2024/", "redirect", Redirect)]);
        result.Source.Details.Identifiers.ShouldBe([new IdentifierCheckDto("url", "redirect", Redirect)]);
        result.Source.Verdict.Action.ShouldBe("set_licence");
        result.Source.History[0].Text.ShouldBe("re-checked URL");
        result.Source.History[0].Who.ShouldBe("Joel Dickson");
    }

    [Test, Order(2)]
    public async Task Accept_WhenNoRecheckReportedThatAddress_ShouldBe400()
    {
        using var response = await Client.PostAsJsonAsync($"/api/references/{SeedIds.References.DoraReport2024}/recheck/accept", new AcceptRedirectRequest("https://elsewhere.test/"));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(3)]
    public async Task Accept_WhenTheRedirectWasReported_ShouldReplaceTheUrlAndVerifyIt()
    {
        var source = await PostSourceAsync(SeedIds.References.DoraReport2024, "recheck/accept", new AcceptRedirectRequest(Redirect));

        source.Details.Url.ShouldBe(Redirect);
        source.Details.Identifiers.ShouldBe([new IdentifierCheckDto("url", "ok", null)]);
        source.History[0].Text.ShouldBe("accepted the new URL address");
    }

    [Test, Order(4)]
    public async Task Recheck_WhenTheAddressIsUnknownToTheLookup_ShouldReportNotFound()
    {
        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.DoraReport2024}/recheck", null);

        var result = (await response.Content.ReadFromJsonAsync<RecheckResultDto>())!;
        result.Fields.Single().Outcome.ShouldBe("not_found");
        result.Source.Checks.Single(c => c.Key == "identifiers").Status.ShouldBe("warn");
        result.Source.Verdict.Reason.ShouldBe("licence_unknown");
    }
}
