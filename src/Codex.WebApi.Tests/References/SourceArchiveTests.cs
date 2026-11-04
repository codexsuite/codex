using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// Archiving the Velocity Trap turns its two quotes back into plain text.
[TestFixture]
public class SourceArchiveTests : SourceApiFixture
{
    private async Task<ProseDto> ProseOfAsync(Guid sceneId) => (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose"))!;

    [Test, Order(1)]
    public async Task Archive_WhenTheSourceIsCited_ShouldUnwrapEveryUseAndListTheScenes()
    {
        (await ProseOfAsync(SeedIds.Scenes.Ch4Sc1)).Body.ShouldContain(SeedIds.ReferenceUses.VelocityTrapCh4Sc1.ToString());
        var source = await GetSourceAsync(SeedIds.References.VelocityTrap);
        var summary = await GetSummaryAsync();

        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.VelocityTrap}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ArchiveSourceResult>())!;
        result.Scenes.Select(s => s.SceneId).ShouldBe([SeedIds.Scenes.Ch4Sc1, SeedIds.Scenes.Ch8Sc1]);
        result.Scenes.ShouldAllBe(s => s.Uses == 1);
        var after4 = (await ProseOfAsync(SeedIds.Scenes.Ch4Sc1)).Body;
        var after8 = (await ProseOfAsync(SeedIds.Scenes.Ch8Sc1)).Body;
        after4.ShouldNotContain(SeedIds.ReferenceUses.VelocityTrapCh4Sc1.ToString());
        after8.ShouldNotContain(SeedIds.ReferenceUses.VelocityTrapCh8Sc1.ToString());
        foreach (var use in source.Uses)
        {
            (use.SceneId == SeedIds.Scenes.Ch4Sc1 ? after4 : after8).ShouldContain(use.Excerpt.Split("\n\n")[0]);
        }

        (await GetSummaryAsync()).Total.ShouldBe(summary.Total - 1);
    }

    [Test, Order(2)]
    public async Task Archive_WhenDone_ShouldRemoveTheSourceFromTheListAndTheDetail()
    {
        using var detail = await Client.GetAsync($"/api/references/{SeedIds.References.VelocityTrap}");
        var rows = (await Client.GetFromJsonAsync<SourceRowDto[]>($"/api/books/{SeedIds.Books.ProductEngineering}/sources"))!;

        detail.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        rows.Select(r => r.Id).ShouldNotContain(SeedIds.References.VelocityTrap);
        (await GetSummaryAsync()).Risks.ShouldBe(2);
    }

    [Test, Order(3)]
    public async Task Archive_WhenRepeated_ShouldBe404()
    {
        using var response = await Client.PostAsync($"/api/references/{SeedIds.References.VelocityTrap}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test, Order(4)]
    public async Task Archive_WhenTheSourceIsUncited_ShouldJustArchiveIt()
    {
        var result = (await (await Client.PostAsync($"/api/references/{SeedIds.References.LeanSoftware}/archive", null)).Content.ReadFromJsonAsync<ArchiveSourceResult>())!;

        result.Scenes.ShouldBeEmpty();
    }
}
