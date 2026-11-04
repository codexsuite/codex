using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.CrossCuttingRules;

// Cross-cutting rule 5: archiving a scene that holds the only use of a source makes the source Not cited (no number,
// no uses) and takes it out of the cited bibliography, but it stays in the sources table and the exports; its marker
// stays in the archived scene's prose, so Restore brings the use, the number and the bibliography entry back.
// Cagan's Empowered is cited once, in Ch 5 Sc 1.
[TestFixture]
public class OnlyUseArchivedRuleTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.ProductEngineering;
    private static readonly Guid Source = SeedIds.References.CaganEmpowered;
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc1;
    private string _body = "";

    private async Task<SourceRowDto> RowAsync() =>
        (await Client.GetFromJsonAsync<SourceRowDto[]>($"/api/books/{Book}/sources", JsonSerializerOptions.Web))!.Single(r => r.Id == Source);

    private async Task<Guid[]> BibliographyAsync() =>
        (await Client.GetFromJsonAsync<SourceCslDto[]>($"/api/books/{Book}/sources/csl", JsonSerializerOptions.Web))!.Select(r => r.SourceId).ToArray();

    [Test, Order(1)]
    public async Task ArchiveScene_WhenItHoldsTheOnlyUse_ShouldMakeTheSourceNotCitedAndLeaveTheBibliography()
    {
        _body = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose", JsonSerializerOptions.Web))!.Body;
        var before = await RowAsync();
        (before.Uses, before.Cited, before.Number is null).ShouldBe((1, true, false));
        (await BibliographyAsync()).ShouldContain(Source);

        using var response = await Client.PostAsync($"/api/scenes/{Scene}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await RowAsync();
        (after.Uses, after.Cited, after.Number, after.Status).ShouldBe((0, false, null, "not_cited"));
        (await BibliographyAsync()).ShouldNotContain(Source);
        (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose", JsonSerializerOptions.Web))!.Body.ShouldBe(_body);
    }

    [Test, Order(2)]
    public async Task ArchiveScene_WhenSourceIsNotCited_ShouldStillExportIt()
    {
        using var response = await Client.GetAsync($"/api/books/{Book}/sources/export?format=bibtex");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Empowered");
    }

    [Test, Order(3)]
    public async Task RestoreScene_WhenItHeldTheOnlyUse_ShouldCiteTheSourceAgain()
    {
        using var response = await Client.PostAsync($"/api/scenes/{Scene}/restore", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var row = await RowAsync();
        (row.Uses, row.Cited, row.Number is null).ShouldBe((1, true, false));
        (await BibliographyAsync()).ShouldContain(Source);
    }
}
