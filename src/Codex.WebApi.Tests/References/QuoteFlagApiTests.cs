using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.References;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.References;
using Codex.TestSupport.Fixtures;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.References;

// The unflagged-quotes inbox on the seed: two open flags (Ch 6 Sc 1 quotation marks, Ch 8 Sc 1 a 93% match). Scans run
// through QuoteFlagScanner.ScanAsync (the handler of ProseSaved) so the tests need no timing.
[TestFixture]
public class QuoteFlagApiTests : ApiFixture
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private const string NewQuote = "we never ship a release on a Friday afternoon";

    private async Task<QuoteFlagDto[]> FlagsAsync() =>
        (await Client.GetFromJsonAsync<QuoteFlagDto[]>($"/api/books/{BookId}/quote-flags"))!;

    private Task ScanAsync(Guid sceneId) =>
        Host.Services.GetRequiredService<QuoteFlagScanner>().ScanAsync(SeedIds.Books.ProductEngineering, sceneId);

    [Test, Order(1)]
    public async Task List_WhenSeeded_ShouldReturnTheTwoOpenFlagsInReadingOrder()
    {
        var flags = await FlagsAsync();

        flags.Select(f => f.Id).ShouldBe([SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote, SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch]);
        flags[0].Reason.ShouldBe("quotation_marks");
        flags[0].Excerpt.ShouldBe("The customer is not in the building");
        (flags[0].ChapterNumber, flags[0].SceneNumber).ShouldBe((6, 1));
        flags[1].Reason.ShouldBe("source_match");
        flags[1].ReferenceId.ShouldBe(SeedIds.References.VelocityTrap);
        flags[1].MatchPercent.ShouldBe(93);
        flags[1].Why.ShouldContain("93% match with");
        flags[1].Why.ShouldEndWith("p. 112");
    }

    [Test, Order(2)]
    public async Task Scan_WhenScenesAreSeeded_ShouldNotDuplicateFlagsOrFlagWrappedQuotes()
    {
        await ScanAsync(SeedIds.Scenes.Ch6Sc1);
        await ScanAsync(SeedIds.Scenes.Ch9Sc1);
        await ScanAsync(SeedIds.Scenes.Ch8Sc1);
        await ScanAsync(SeedIds.Scenes.Ch4Sc1);

        (await FlagsAsync()).Select(f => f.Id).ShouldBe([SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote, SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch]);
    }

    [Test, Order(3)]
    public async Task ProsePut_WithAQuotedSentence_ShouldRaiseAFlagOnScan()
    {
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch7Sc1}/prose"))!;
        using var put = await Client.PutAsJsonAsync($"/api/scenes/{SeedIds.Scenes.Ch7Sc1}/prose",
            new SaveProseRequest(prose.Body + $"\nOne lead insists \"{NewQuote}\" every sprint.\n", prose.Version));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        await ScanAsync(SeedIds.Scenes.Ch7Sc1);

        var flag = (await FlagsAsync()).Single(f => f.Excerpt == NewQuote);
        flag.Reason.ShouldBe("quotation_marks");
        flag.SceneId.ShouldBe(SeedIds.Scenes.Ch7Sc1);
        (flag.ChapterNumber, flag.SceneNumber).ShouldBe((7, 1));
    }

    [Test, Order(4)]
    public async Task NotAQuote_WhenResolved_ShouldNeverReturnOnAnotherScan()
    {
        using var response = await Client.PostAsync($"/api/quote-flags/{SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote}/not-a-quote", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<QuoteFlagDto[]>())!.Select(f => f.Id).ShouldNotContain(SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote);

        await ScanAsync(SeedIds.Scenes.Ch6Sc1);

        (await FlagsAsync()).Select(f => f.Excerpt).ShouldNotContain("The customer is not in the building");
        await using var connection = await OpenDatabaseAsync();
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM quote_flag WHERE excerpt = 'The customer is not in the building'")).ShouldBe(1);
        (await connection.ExecuteScalarAsync<string>("SELECT status FROM quote_flag WHERE id = @id", new { id = SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote })).ShouldBe("dismissed");
    }

    [Test, Order(5)]
    public async Task NotAQuote_WhenAlreadyResolved_ShouldBe409()
    {
        using var response = await Client.PostAsync($"/api/quote-flags/{SeedIds.QuoteFlags.Ch6Sc1UnmarkedQuote}/not-a-quote", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe("conflict");
    }

    [Test, Order(6)]
    public async Task OtherSource_WhenNoteIsBlank_ShouldBe400AndKeepTheFlag()
    {
        using var response = await Client.PostAsJsonAsync($"/api/quote-flags/{SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch}/other-source", new OtherSourceRequest("  "));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
        (await FlagsAsync()).Select(f => f.Id).ShouldContain(SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch);
    }

    [Test, Order(7)]
    public async Task OtherSource_WhenNoted_ShouldResolveTheFlagAndKeepTheNote()
    {
        using var response = await Client.PostAsJsonAsync($"/api/quote-flags/{SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch}/other-source", new OtherSourceRequest(" Reinertsen, 2009 "));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<QuoteFlagDto[]>())!.Select(f => f.Excerpt).ShouldBe([NewQuote]);
        await using var connection = await OpenDatabaseAsync();
        (await connection.ExecuteScalarAsync<string>("SELECT note FROM quote_flag WHERE id = @id", new { id = SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch })).ShouldBe("Reinertsen, 2009");
    }

    [Test, Order(8)]
    public async Task Resolve_WhenTheFlagOrBookIsUnknown_ShouldBe404()
    {
        using var flag = await Client.PostAsync($"/api/quote-flags/{Guid.NewGuid()}/not-a-quote", null);
        using var other = await Client.PostAsJsonAsync($"/api/quote-flags/{Guid.NewGuid()}/other-source", new OtherSourceRequest("x"));
        using var book = await Client.GetAsync($"/api/books/{Guid.NewGuid()}/quote-flags");

        (await flag.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        (await other.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
        (await book.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe("not_found");
    }

    [Test, Order(9)]
    public async Task Scan_WhenTwoQuotesDifferOnlyInCaseOrPunctuation_ShouldRaiseOneFlagAndNotFail()
    {
        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{SeedIds.Scenes.Ch4Sc1}/prose"))!;
        using var put = await Client.PutAsJsonAsync($"/api/scenes/{SeedIds.Scenes.Ch4Sc1}/prose",
            new SaveProseRequest(prose.Body + "\nOne says \"The end is near, friend.\" and another \"the end is NEAR friend\" in the same breath.\n", prose.Version));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        await ScanAsync(SeedIds.Scenes.Ch4Sc1);

        (await FlagsAsync()).Count(f => f.Excerpt.StartsWith("The end is near", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        await ScanAsync(SeedIds.Scenes.Ch4Sc1);
        (await FlagsAsync()).Count(f => f.Excerpt.StartsWith("The end is near", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }
}
