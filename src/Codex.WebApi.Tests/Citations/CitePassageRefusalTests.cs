using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Citations;

// Refused citations write nothing: no reference_use row and no new prose version.
[TestFixture]
public class CitePassageRefusalTests : ApiFixture
{
    private static readonly Guid Scene = SeedIds.Scenes.Ch7Sc1;
    private ProseDto _prose = null!;
    private string _sentence = "";

    [OneTimeSetUp]
    public async Task ReadSceneAsync()
    {
        _prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{Scene}/prose", JsonSerializerOptions.Web))!;
        var text = _prose.Body.Split("\n\n")[1];
        _sentence = text[..(text.IndexOf('.') + 1)];
    }

    private async Task<(int Uses, int Version)> StateAsync()
    {
        await using var db = await OpenDatabaseAsync();
        return await db.QuerySingleAsync<(int, int)>(
            "SELECT (SELECT count(*)::int FROM reference_use), (SELECT version FROM prose WHERE scene_id = @scene)", new { scene = Scene });
    }

    private async Task<string> RefusedAsync(CitePassageRequest request, HttpStatusCode status)
    {
        var before = await StateAsync();
        var response = await Client.PostAsJsonAsync("/api/citations", request);
        var error = await response.ReadApiErrorAsync(status);
        (await StateAsync()).ShouldBe(before);
        return error.Code;
    }

    [Test]
    public async Task Cite_WhenTheVersionIsStale_ShouldBeConflictAndWriteNothing()
    {
        var code = await RefusedAsync(new CitePassageRequest(Scene, SeedIds.References.Accelerate, _prose.Version - 1, _sentence), HttpStatusCode.Conflict);

        code.ShouldBe("conflict");
    }

    [Test]
    public async Task Cite_WhenTheSourceIsFromAnotherBook_ShouldBeValidation()
    {
        Guid elsewhere;
        await using (var db = await OpenDatabaseAsync())
        {
            elsewhere = await TestRows.SourceAsync(db, SeedIds.Books.PlayersGuide, "Elsewhere", "cc_by_4");
        }

        (await RefusedAsync(new CitePassageRequest(Scene, elsewhere, _prose.Version, _sentence), HttpStatusCode.BadRequest)).ShouldBe("validation");
    }

    [Test]
    public async Task Cite_WhenThePassageIsNotInTheBody_ShouldBeValidation()
    {
        var code = await RefusedAsync(new CitePassageRequest(Scene, SeedIds.References.Accelerate, _prose.Version, "Words this scene never had."), HttpStatusCode.BadRequest);

        code.ShouldBe("validation");
    }

    [Test]
    public async Task Cite_WhenThePassageIsAlreadyMarked_ShouldBeValidation()
    {
        var code = await RefusedAsync(new CitePassageRequest(Scene, SeedIds.References.Accelerate, _prose.Version, "Thin slices, feature flags", Occurrence: 1), HttpStatusCode.BadRequest);

        code.ShouldBe("validation");
    }

    [Test]
    public async Task Cite_WhenTheSourceIsUnknown_ShouldBeNotFound()
    {
        var code = await RefusedAsync(new CitePassageRequest(Scene, Guid.NewGuid(), _prose.Version, _sentence), HttpStatusCode.NotFound);

        code.ShouldBe("not_found");
    }

    [Test]
    public async Task Cite_WhenTheKindIsUnknown_ShouldBeValidation()
    {
        var code = await RefusedAsync(new CitePassageRequest(Scene, SeedIds.References.Accelerate, _prose.Version, _sentence, Kind: "rumour"), HttpStatusCode.BadRequest);

        code.ShouldBe("validation");
    }

    [Test]
    public async Task Cite_WhenThePassageIsTooLong_ShouldBeValidation()
    {
        var code = await RefusedAsync(new CitePassageRequest(Scene, SeedIds.References.Accelerate, _prose.Version, new string('a', 2001)), HttpStatusCode.BadRequest);

        code.ShouldBe("validation");
    }

    [Test]
    public async Task Cite_WhenTheLocatorIsTooLong_ShouldBeValidation()
    {
        var code = await RefusedAsync(new CitePassageRequest(Scene, SeedIds.References.Accelerate, _prose.Version, _sentence, Locator: new string('p', 201)), HttpStatusCode.BadRequest);

        code.ShouldBe("validation");
    }

    private async Task<(bool Archived, int Version)> UseStateAsync(Guid useId)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.QuerySingleAsync<(bool, int)>(
            "SELECT archived_at IS NOT NULL, (SELECT version FROM prose WHERE scene_id = @scene) FROM reference_use WHERE id = @useId",
            new { scene = Scene, useId });
    }

    [Test]
    public async Task Remove_WhenTheVersionIsStale_ShouldBeConflictAndKeepTheMarker()
    {
        var use = SeedIds.ReferenceUses.AccelerateCh7Sc1;
        var before = await UseStateAsync(use);

        var response = await Client.PostAsJsonAsync($"/api/citations/{use}/remove", new RemoveCitationRequest(before.Version - 1));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe("conflict");
        (await UseStateAsync(use)).ShouldBe(before);
    }

    [Test]
    public async Task Remove_WhenTheBodyHasNoLinkForTheUse_ShouldBeNotFoundAndWriteNothing()
    {
        Guid use;
        await using (var db = await OpenDatabaseAsync())
        {
            use = await TestRows.UseAsync(db, SeedIds.Books.ProductEngineering, SeedIds.References.Accelerate, Scene, "Not in the prose.");
        }

        var before = await UseStateAsync(use);

        var response = await Client.PostAsJsonAsync($"/api/citations/{use}/remove", new RemoveCitationRequest(before.Version));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        (await UseStateAsync(use)).ShouldBe(before);
    }
}
