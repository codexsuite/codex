using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.Contracts.Sources;
using Codex.Core;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.References;

// Look up an ISBN over the fake provider, add the source from it, then meet it again as a duplicate.
[TestFixture]
public class AddSourceApiTests : SourceApiFixture
{
    private const string Isbn = "9780140449136";
    private static readonly string Book = SeedIds.Books.ProductEngineering.ToString();
    private static Guid _added;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    private async Task<HttpResponseMessage> LookupAsync(string kind, string value) =>
        await Client.PostAsJsonAsync($"/api/books/{Book}/references/lookup", new LookupSourceRequest(kind, value));

    [Test, Order(1)]
    public async Task Lookup_WhenIsbnIsNewToTheBook_ShouldReturnFoundWithTheCanonicalIsbn()
    {
        using var response = await LookupAsync("isbn", "978-0-14-044913-6");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<LookupSourceResultDto>())!;
        result.Outcome.ShouldBe("found");
        result.Value.ShouldBe(Isbn);
        result.Source.ShouldNotBeNull().Title.ShouldBe("The Histories");
    }

    [Test, Order(2)]
    public async Task Add_WhenFromTheLookup_ShouldCreateTheSourceAndRecordWhereItCameFrom()
    {
        var details = new SourceDetailsRequest("book", "The Histories", "Herodotus", 2003, "Penguin Classics", null, Isbn, null, null, null, "unknown", null);

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references", new AddSourceRequest(details, "isbn"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        _added = (await response.Content.ReadFromJsonAsync<AddSourceResult>())!.Id.ShouldNotBeNull();
        var source = await GetSourceAsync(_added);
        source.Details.Isbn.ShouldBe(Isbn);
        source.Details.AuthorsText.ShouldBe("Herodotus");
        source.History[0].Text.ShouldBe("added the source from ISBN");
    }

    [Test, Order(3)]
    public async Task Lookup_WhenTheBookAlreadyHasTheIsbn_ShouldOfferTheExistingSource()
    {
        using var response = await LookupAsync("isbn", Isbn);

        var result = (await response.Content.ReadFromJsonAsync<LookupSourceResultDto>())!;
        result.Outcome.ShouldBe("existing");
        result.ExistingId.ShouldBe(_added);
        result.ExistingTitle.ShouldBe("The Histories");
        result.ExistingByline.ShouldBe("Herodotus · 2003");
    }

    [Test, Order(4)]
    public async Task Add_WhenTheIsbnIsAlreadyThere_ShouldReturnTheExistingIdAndCreateNothing()
    {
        var details = new SourceDetailsRequest("book", "Same book again", "", null, null, null, "978-0-14-044913-6", null, null, null, "unknown", null);
        var before = (await GetSummaryAsync()).Total;

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references", new AddSourceRequest(details, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AddSourceResult>())!.ShouldBe(new AddSourceResult(null, _added));
        (await GetSummaryAsync()).Total.ShouldBe(before);
    }

    [Test, Order(5)]
    public async Task Lookup_WhenTheProviderKnowsNothing_ShouldReturnNotFound()
    {
        using var response = await LookupAsync("doi", "10.9999/nobody-knows");

        (await response.Content.ReadFromJsonAsync<LookupSourceResultDto>())!.Outcome.ShouldBe("not_found");
    }

    [Test, Order(6)]
    public async Task Lookup_WhenTheUrlMoved_ShouldReturnTheNewAddress()
    {
        using var response = await LookupAsync("url", "https://example.test/old-address");

        var result = (await response.Content.ReadFromJsonAsync<LookupSourceResultDto>())!;
        (result.Outcome, result.RedirectTo).ShouldBe(("redirect", "https://example.test/article"));
    }

    [Test, Order(7)]
    public async Task Add_WhenTypedByHandWithoutIdentifiers_ShouldCreateIt()
    {
        var details = new SourceDetailsRequest("paper", "A paper with no identifiers", "Doe, Jane; Roe, Richard", 1999, null, null, null, null, null, null, "unknown", null);

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references", new AddSourceRequest(details, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var source = await GetSourceAsync((await response.Content.ReadFromJsonAsync<AddSourceResult>())!.Id!.Value);
        (source.Details.Kind, source.Details.AuthorsText, source.History[0].Text).ShouldBe(("paper", "Doe, Jane; Roe, Richard", "added the source"));
    }

    [TestCase("isbn", "9780140449130")]
    [TestCase("isbn", "")]
    [TestCase("doi", "not a doi")]
    [TestCase("url", "ftp://example.test/file")]
    [TestCase("pigeon", "x")]
    public async Task Lookup_WhenTheValueIsMalformed_ShouldBe400Validation(string kind, string value)
    {
        using var response = await LookupAsync(kind, value);

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Lookup_WhenTheBookIsUnknown_ShouldBe404()
    {
        using var response = await Client.PostAsJsonAsync($"/api/books/{Guid.NewGuid()}/references/lookup", new LookupSourceRequest("isbn", Isbn));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Add_WhenTheTitleIsBlank_ShouldBe400Validation()
    {
        var details = new SourceDetailsRequest("book", "  ", "", null, null, null, null, null, null, null, "unknown", null);

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references", new AddSourceRequest(details, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task Add_WhenTheKindIsUnknown_ShouldBe400Validation()
    {
        var details = new SourceDetailsRequest("pamphlet", "A title", "", null, null, null, null, null, null, null, "unknown", null);

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references", new AddSourceRequest(details, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test, Order(90)]
    public async Task Add_WhenAWebSourceHasNoAccessedDate_ShouldDefaultItToTodayFromTheClock()
    {
        var details = new SourceDetailsRequest("web", "A page", "", null, null, null, null, null, "https://example.test/accessed-default", null, "unknown", null);

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references", new AddSourceRequest(details, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<AddSourceResult>())!.Id.ShouldNotBeNull();
        var today = DisplayDate.Today(TimeProvider.System.GetUtcNow(), TimeProvider.System.LocalTimeZone);
        (await GetSourceAsync(id)).Details.AccessedOn.ShouldBe(today.ToString("yyyy-MM-dd"));
    }

    [Test, Order(91)]
    [TestCase("30/10/2026")]
    [TestCase("2026-02-30")]
    [TestCase(" 2026-10-19")]
    [TestCase("1899-12-31")]
    public async Task Add_WhenTheAccessedDateIsBad_ShouldBe400(string accessedOn)
    {
        var details = new SourceDetailsRequest("web", "Bad date", "", null, null, null, null, null, "https://example.test/bad-date", accessedOn, "unknown", null);

        using var response = await Client.PostAsJsonAsync($"/api/books/{Book}/references", new AddSourceRequest(details, null));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }
}
