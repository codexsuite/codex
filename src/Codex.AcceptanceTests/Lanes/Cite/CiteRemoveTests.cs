using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.Contracts;
using Codex.Contracts.Citations;

namespace Codex.AcceptanceTests.Lanes.Cite;

// Removing a marker from its card: Lean Software Development is cited through the API (CiteFirstUseTests covers the
// picker), then its card's Remove archives the use and the later sources move back down.
[TestFixture]
public class CiteRemoveTests : CiteLaneTest
{
    private async Task<Guid> CiteLeanThroughTheApiAsync()
    {
        var prose = await GetProseAsync();
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var response = await client.PostAsJsonAsync(
            "/api/citations", new CitePassageRequest(Scene, SeedIds.References.LeanSoftware, prose.Version, Sentence(prose.Body, 2)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CitePassageResult>(Json))!.UseId;
    }

    [Test]
    public async Task Remove_ShouldArchiveTheUseAndMoveLaterMarkersBack()
    {
        var lean = await CiteLeanThroughTheApiAsync();
        var write = await OpenAsync();
        var cite = new CitePage(Page);
        await Expect(write.Marker(SeedIds.ReferenceUses.TeamTopologiesCh9Sc1)).ToHaveAttributeAsync("data-number", "10");
        await write.Marker(lean).ClickAsync();
        await Expect(write.MarkerCardSource).ToContainTextAsync("Lean Software Development");

        var response = await Page.RunAndWaitForApiAsync($"/api/citations/{lean}/remove", "POST", () => cite.Remove.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(write.Marker(lean)).ToHaveCountAsync(0);
        await Expect(write.MarkerCard).ToHaveCountAsync(0);
        await Expect(write.Marker(SeedIds.ReferenceUses.TeamTopologiesCh9Sc1)).ToHaveAttributeAsync("data-number", "9");
        await Expect(write.Marker(SeedIds.ReferenceUses.ShipItFridayCh10Sc1)).ToHaveAttributeAsync("data-number", "11");
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NOT NULL FROM reference_use WHERE id = @id", ("id", lean))).ShouldBeTrue();
        (await GetProseAsync()).Body.ShouldNotContain(lean.ToString());
    }
}
