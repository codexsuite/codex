using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.Contracts;
using Codex.Contracts.Citations;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Citations;

// A flag keeps its words as plain text (QuoteFinder): emphasis, escapes and link brackets gone. Citing it finds those
// words in the stored Markdown, prefers the quoted occurrence, and refuses (writing nothing) words that are gone, sit
// inside a marker already, hold a mention, or a source from another book. Each test has its own scene.
[TestFixture]
public class CiteFlagMatchingApiTests : ApiFixture
{
    private static readonly Guid Book = SeedIds.Books.ProductEngineering;
    private static readonly Guid Source = SeedIds.References.VelocityTrap;
    private const string Words = "the customer is never in the building at all";

    private async Task<int> PutAsync(Guid sceneId, string body)
    {
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose", JsonSerializerOptions.Web))!;
        var put = await Client.PutAsJsonAsync($"/api/scenes/{sceneId}/prose", new SaveProseRequest(body, prose.Version));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        return prose.Version + 1;
    }

    // The scanner may raise the same words itself, and the words are one fingerprint per book (another test may have
    // cited them in its scene): either way there is one open flag on this scene with the guessed source.
    private async Task<Guid> FlagAsync(Guid sceneId, string excerpt)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.QuerySingleAsync<Guid>(
            """
            INSERT INTO quote_flag (book_id, scene_id, excerpt, reason, reference_id)
            VALUES (@Book, @sceneId, @excerpt, 'quotation_marks', @Source)
            ON CONFLICT (book_id, fingerprint) DO UPDATE
                SET reference_id = EXCLUDED.reference_id, scene_id = EXCLUDED.scene_id, status = 'open', resolved_use_id = NULL
            RETURNING id
            """,
            new { Book, sceneId, excerpt, Source });
    }

    private async Task<(int Uses, int Version)> StateAsync(Guid sceneId)
    {
        await using var db = await OpenDatabaseAsync();
        return await db.QuerySingleAsync<(int, int)>(
            "SELECT (SELECT count(*)::int FROM reference_use), (SELECT version FROM prose WHERE scene_id = @sceneId)", new { sceneId });
    }

    private async Task<CitePassageResult> CitedAsync(Guid flagId)
    {
        var response = await Client.PostAsJsonAsync($"/api/quote-flags/{flagId}/cite", new CiteFlagRequest());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CitePassageResult>(JsonSerializerOptions.Web))!;
    }

    // The refusal's code, after checking that nothing was written.
    private async Task<string> RefusedAsync(Guid sceneId, Guid flagId, Guid? sourceId = null)
    {
        var before = await StateAsync(sceneId);
        var response = await Client.PostAsJsonAsync($"/api/quote-flags/{flagId}/cite", new CiteFlagRequest(sourceId));
        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        (await StateAsync(sceneId)).ShouldBe(before);
        return error.Code;
    }

    [Test]
    public async Task CiteFlag_WhenTheQuoteHasItalics_ShouldWrapTheMarkdownBetweenTheQuotationMarks()
    {
        var scene = SeedIds.Scenes.Ch1Sc1;
        await PutAsync(scene, "He said \"the *customer* is never in the building at all\" and left.\n");

        var result = await CitedAsync(await FlagAsync(scene, Words));

        result.Body.ShouldBe($"He said \"[the *customer* is never in the building at all](ref:{result.UseId})\" and left.\n");
    }

    [Test]
    public async Task CiteFlag_WhenTheWordsAppearTwice_ShouldWrapTheQuotedOccurrence()
    {
        var scene = SeedIds.Scenes.Ch2Sc1;
        await PutAsync(scene, $"Note that {Words}. Later: \"{Words}\" again.\n");

        var result = await CitedAsync(await FlagAsync(scene, Words));

        result.Body.ShouldBe($"Note that {Words}. Later: \"[{Words}](ref:{result.UseId})\" again.\n");
    }

    [Test]
    public async Task CiteFlag_WhenTheQuoteHoldsAMention_ShouldBeValidationAndWriteNothing()
    {
        var scene = SeedIds.Scenes.Ch3Sc3;
        await PutAsync(scene, $"He said \"the [customer](kb:{SeedIds.KbEntries.KentBeck}) is never in the building at all\".\n");

        (await RefusedAsync(scene, await FlagAsync(scene, Words))).ShouldBe("validation");
    }

    [Test]
    public async Task CiteFlag_WhenTheWordsAreGone_ShouldBeValidationAndWriteNothing()
    {
        var scene = SeedIds.Scenes.Ch6Sc2;
        await PutAsync(scene, "Nothing quoted here any more.\n");

        (await RefusedAsync(scene, await FlagAsync(scene, Words))).ShouldBe("validation");
    }

    [Test]
    public async Task CiteFlag_WhenTheWordsAreInsideAMarker_ShouldBeValidationAndWriteNothing()
    {
        // Ch 4 Sc 1's Velocity Trap quote is wrapped already.
        var scene = SeedIds.Scenes.Ch4Sc1;

        (await RefusedAsync(scene, await FlagAsync(scene, "Velocity measures how far a team moved"))).ShouldBe("validation");
    }

    [Test]
    public async Task CiteFlag_WhenTheSourceIsFromAnotherBook_ShouldBeValidationAndWriteNothing()
    {
        Guid elsewhere;
        await using (var db = await OpenDatabaseAsync())
        {
            elsewhere = await TestRows.SourceAsync(db, SeedIds.Books.PlayersGuide, "Elsewhere");
        }

        (await RefusedAsync(SeedIds.Scenes.Ch8Sc1, SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch, elsewhere)).ShouldBe("validation");
    }
}
