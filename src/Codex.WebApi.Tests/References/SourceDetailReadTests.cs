using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.References;

[TestFixture]
public class SourceDetailReadTests : SourceApiFixture
{
    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new UnavailableSourceLookup());

    [Test]
    public async Task Get_WhenVelocityTrap_ShouldBeARiskWithItsTwoUsesInReadingOrder()
    {
        var source = await GetSourceAsync(SeedIds.References.VelocityTrap);

        (source.Number, source.Verdict.Status, source.Verdict.Reason, source.Verdict.Action).ShouldBe((4, "risk", "quote_over_limit", "permission"));
        (source.QuotedWords, source.Limit).ShouldBe((342, 300));
        source.Uses.Select(u => u.Id).ShouldBe([SeedIds.ReferenceUses.VelocityTrapCh4Sc1, SeedIds.ReferenceUses.VelocityTrapCh8Sc1]);
        source.Uses.ShouldAllBe(u => u.Kind == "quote" && u.Excerpt.Length > 0);
        source.Checks.Select(c => c.Key).ShouldBe(["words", "lyrics", "figures", "data", "licence", "citation", "identifiers"]);
        source.Checks.Single(c => c.Key == "words").Status.ShouldBe("danger");
        source.Details.AuthorsText.ShouldBe("Whitlock, Dana");
        source.Kinds.Select(k => k.Code).ShouldContain("book");
        source.Licences.Select(l => l.Code).ShouldContain("public_domain");
    }

    [Test]
    public async Task Get_WhenAccelerate_ShouldMarkTheOneUseNewSinceSignOff()
    {
        var source = await GetSourceAsync(SeedIds.References.Accelerate);

        (source.Verdict.Status, source.Verdict.Reason, source.Verdict.Action).ShouldBe(("needs_check", "new_uses_since_signoff", "sign_off"));
        source.Verdict.SignedOffBy.ShouldBe("Joel Dickson");
        source.Verdict.SignedOffLabel.ShouldNotBeNullOrEmpty();
        source.Uses.Count(u => u.NewSinceSignOff).ShouldBe(1);
    }

    [Test]
    public async Task Get_WhenDora_ShouldAskForARecheckOfTheRedirectingAddress()
    {
        var source = await GetSourceAsync(SeedIds.References.DoraReport2024);

        source.Details.Identifiers.ShouldBe([new IdentifierCheckDto("url", "redirect", "https://dora.dev/research/2024/dora-report/")]);
    }

    [Test]
    public async Task Get_WhenTheUncitedSource_ShouldOfferToRemoveIt()
    {
        var source = await GetSourceAsync(SeedIds.References.LeanSoftware);

        (source.Number, source.Verdict.Status, source.Verdict.Action, source.Uses.Count).ShouldBe((null, "not_cited", "remove", 0));
    }

    [Test]
    public async Task Get_WhenUnknown_ShouldBe404()
    {
        using var response = await Client.GetAsync($"/api/references/{Guid.NewGuid()}");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task PatchUse_WhenUnknownKind_ShouldBe400()
    {
        using var response = await Client.PatchAsJsonAsync($"/api/reference-uses/{SeedIds.ReferenceUses.VelocityTrapCh4Sc1}", new PatchUseRequest("essay"));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task PatchUse_WhenUnknownUse_ShouldBe404()
    {
        using var response = await Client.PatchAsJsonAsync($"/api/reference-uses/{Guid.NewGuid()}", new PatchUseRequest("quote"));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task SignOff_WhenARiskIsOpen_ShouldBe422Blocked()
    {
        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.VelocityTrap}/sign-off", null);

        (await response.ReadApiErrorAsync((HttpStatusCode)422)).Code.ShouldBe(ApiErrorCodes.Blocked);
        (await GetSourceAsync(SeedIds.References.VelocityTrap)).Verdict.SignedOffLabel.ShouldBeNull();
    }

    [Test]
    public async Task Recheck_WhenNoLookupIsConfigured_ShouldBe503NotConfigured()
    {
        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.DoraReport2024}/recheck", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.ServiceUnavailable)).Code.ShouldBe(ApiErrorCodes.NotConfigured);
    }
}
