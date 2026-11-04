using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Lanes.References;

// With no lookup provider configured (an unavailable lookup), Re-check says so and changes nothing.
[TestFixture]
public class ClearanceNotConfiguredTests : ReferencesLaneTest
{
    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new UnavailableSourceLookup());

    [Test, Order(1)]
    public async Task Recheck_WhenNoLookupIsConfigured_ShouldShowTheMessageAndKeepTheDetails()
    {
        await Page.GotoAsync(SourceLink(SeedIds.References.DoraReport2024));
        var panel = new SourcePanel(Page);
        await Expect(panel.Verification("url")).ToHaveTextAsync("Redirects");

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{SeedIds.References.DoraReport2024}/recheck", "POST", () => panel.Recheck.ClickAsync());

        response.Status.ShouldBe(503);
        await Expect(panel.Banner).ToContainTextAsync("not configured");
        await Expect(panel.Verification("url")).ToHaveTextAsync("Redirects");
    }
}
