using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Citations;

// J11 step 3 on the seed: cite f2 (Ch 8 Sc 1, The Velocity Trap, p. 112). Its 34 words are wrapped as a quote use and
// the flag is marked cited with it; a second cite is a conflict; f1 has no guessed source, so it needs one.
[TestFixture]
public class CiteFlagApiTests : ApiFixture
{
    private static readonly Guid F1 = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote;
    private static readonly Guid F2 = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch;

    private async Task<(int Uses, int Version, string Status)> StateAsync(Guid flagId)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.QuerySingleAsync<(int, int, string)>(
            """
            SELECT (SELECT count(*)::int FROM reference_use),
                   (SELECT version FROM prose p WHERE p.scene_id = f.scene_id), f.status
            FROM quote_flag f WHERE f.id = @flagId
            """,
            new { flagId });
    }

    [Test, Order(1)]
    public async Task CiteFlag_WhenF2IsCited_ShouldAddA34WordQuoteAndResolveTheFlag()
    {
        string excerpt;
        await using (var db = await OpenDatabaseAsync())
        {
            excerpt = await db.QuerySingleAsync<string>("SELECT excerpt FROM quote_flag WHERE id = @F2", new { F2 });
        }

        var response = await Client.PostAsJsonAsync($"/api/quote-flags/{F2}/cite", new CiteFlagRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<CitePassageResult>(JsonSerializerOptions.Web))!;
        result.SceneId.ShouldBe(SeedIds.Scenes.Ch8Sc1);
        result.Status.ShouldBe("risk");
        result.Body.ShouldContain($"[{excerpt}](ref:{result.UseId})");
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/prose", JsonSerializerOptions.Web))!;
        prose.Body.ShouldBe(result.Body);
        await using var check = await OpenDatabaseAsync();
        var use = await check.QuerySingleAsync<(string Kind, string? Locator, int Words, Guid Source)>(
            "SELECT kind, locator, word_count, reference_id FROM reference_use WHERE id = @UseId", new { result.UseId });
        use.ShouldBe(("quote", "p. 112", 34, SeedIds.References.VelocityTrap));
        var flag = await check.QuerySingleAsync<(string Status, Guid? Use)>(
            "SELECT status, resolved_use_id FROM quote_flag WHERE id = @F2", new { F2 });
        flag.ShouldBe(("cited", (Guid?)result.UseId));
        (await check.QuerySingleAsync<int>(
            "SELECT quoted_words FROM reference_stats WHERE reference_id = @VelocityTrap", new { SeedIds.References.VelocityTrap }))
            .ShouldBe(376);
        (await Client.GetFromJsonAsync<List<JsonElement>>($"/api/books/{SeedIds.Books.ProductEngineering}/quote-flags", JsonSerializerOptions.Web))!
            .Select(f => f.GetProperty("id").GetGuid()).ShouldBe([F1]);
    }

    [Test, Order(2)]
    public async Task CiteFlag_WhenTheFlagIsAlreadyCited_ShouldBeConflictAndWriteNothing()
    {
        var before = await StateAsync(F2);

        var response = await Client.PostAsJsonAsync($"/api/quote-flags/{F2}/cite", new CiteFlagRequest());

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe("conflict");
        (await StateAsync(F2)).ShouldBe(before);
    }

    [Test, Order(3)]
    public async Task CiteFlag_WhenNoSourceIsGivenOrGuessed_ShouldBeValidationAndWriteNothing()
    {
        var before = await StateAsync(F1);

        var response = await Client.PostAsJsonAsync($"/api/quote-flags/{F1}/cite", new CiteFlagRequest());

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await StateAsync(F1)).ShouldBe(before);
    }

    [Test, Order(4)]
    public async Task CiteFlag_WhenTheFlagIsUnknown_ShouldBeNotFound()
    {
        var response = await Client.PostAsJsonAsync($"/api/quote-flags/{Guid.NewGuid()}/cite", new CiteFlagRequest());

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }
}
