using Codex.Application.Network;
using System.Net;
using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.Sources;
using Codex.TestSupport.Contracts;

namespace Codex.UnitTest.Contracts;

// The real provider over a stubbed HTTP handler: it passes the shared contract, then the cases only it has.
[TestFixture]
public class HttpSourceLookupTests : SourceLookupContract
{
    private const string Public = "93.184.216.34";
    private const string OpenLibrary = "https://openlibrary.org/api/books?bibkeys=ISBN:";
    private StubHttpHandler _http = null!;

    [SetUp]
    public void SetUp()
    {
        _http = new StubHttpHandler();
        _http.Json($"{OpenLibrary}9780140449136&format=json&jscmd=data",
            """{"ISBN:9780140449136":{"title":"The Histories","authors":[{"name":"Herodotus"}],"publishers":[{"name":"Penguin Classics"}],"publish_date":"March 2003"}}""");
        _http.Json("https://api.crossref.org/works/10.1000/xyz123",
            """
            {"message":{"title":["Sleep and Memory Consolidation"],"author":[{"given":"Ada","family":"Lovelace"},{"name":"The Society"}],
             "issued":{"date-parts":[[2019,5]]},"container-title":["Journal of Imaginary Studies"],"URL":"https://doi.org/10.1000/xyz123"}}
            """);
        _http.Status("https://api.crossref.org/works/10.9999/nobody-knows", HttpStatusCode.NotFound);
        _http.Redirect("https://example.test/old-address", "https://example.test/article");
        _http.Html("https://example.test/article", "<html><head><title> An Example\n Article </title></head></html>");
    }

    [TearDown]
    public void TearDown() => _http.Dispose();

    protected override ISourceLookup Create() => new HttpSourceLookup(_http, new FixedHostResolver(Public));

    [Test]
    public async Task LookupAsync_WhenIsbnIsKnown_ShouldReadTitleAuthorYearAndPublisher()
    {
        var source = (await Create().LookupAsync(SourceLookupKinds.Isbn, KnownIsbn, CancellationToken.None)).Source.ShouldNotBeNull();

        source.Title.ShouldBe("The Histories");
        source.Authors.ShouldBe(["Herodotus"]);
        source.Year.ShouldBe(2003);
        source.Publisher.ShouldBe("Penguin Classics");
    }

    [Test]
    public async Task LookupAsync_WhenDoiIsKnown_ShouldWriteAuthorsFamilyFirstAndKeepOrganisations()
    {
        var source = (await Create().LookupAsync(SourceLookupKinds.Doi, KnownDoi, CancellationToken.None)).Source.ShouldNotBeNull();

        source.Authors.ShouldBe(["Lovelace, Ada", "The Society"]);
        source.Year.ShouldBe(2019);
        source.Publisher.ShouldBe("Journal of Imaginary Studies");
    }

    [Test]
    public async Task LookupAsync_WhenIsbnIsUnknownToOpenLibrary_ShouldReturnNotFound()
    {
        _http.Json($"{OpenLibrary}9781234567897&format=json&jscmd=data", "{}");

        var result = await Create().LookupAsync(SourceLookupKinds.Isbn, "9781234567897", CancellationToken.None);

        result.Outcome.ShouldBe(SourceLookupResult.NotFoundOutcome);
    }

    [Test]
    public async Task LookupAsync_WhenUrlAnswers_ShouldReadThePageTitle()
    {
        var result = await Create().LookupAsync(SourceLookupKinds.Url, "https://example.test/article", CancellationToken.None);

        var source = result.Source.ShouldNotBeNull();
        source.Title.ShouldBe("An Example Article");
        source.Url.ShouldBe("https://example.test/article");
    }

    [Test]
    public async Task LookupAsync_WhenUrlIsGone_ShouldReturnNotFound()
    {
        _http.Status("https://example.test/gone", HttpStatusCode.Gone);

        var result = await Create().LookupAsync(SourceLookupKinds.Url, "https://example.test/gone", CancellationToken.None);

        result.Outcome.ShouldBe(SourceLookupResult.NotFoundOutcome);
    }

    [Test]
    public async Task LookupAsync_WhenUrlResolvesToAPrivateAddress_ShouldRefuseWithoutSendingARequest()
    {
        var lookup = new HttpSourceLookup(_http, new FixedHostResolver(Public, new() { ["intranet.test"] = "10.0.0.5" }));

        var error = await Should.ThrowAsync<ApiException>(
            async () => await lookup.LookupAsync(SourceLookupKinds.Url, "https://intranet.test/secret", CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.Validation);
        _http.Requests.ShouldBeEmpty();
    }

    [TestCase("127.0.0.1")]
    [TestCase("169.254.169.254")]
    [TestCase("192.168.1.10")]
    [TestCase("172.16.0.1")]
    [TestCase("100.64.0.1")]
    [TestCase("::1")]
    [TestCase("fe80::1")]
    [TestCase("fd00::1")]
    [TestCase("::ffff:10.0.0.1")]
    [TestCase("::ffff:127.0.0.1")]
    [TestCase("::ffff:169.254.169.254")]
    [TestCase("::ffff:192.168.0.1")]
    [TestCase("::ffff:172.16.5.5")]
    [TestCase("::ffff:100.64.0.1")]
    [TestCase("10.1.2.3")]
    [TestCase("172.31.255.255")]
    [TestCase("0.0.0.0")]
    [TestCase("::")]
    [TestCase("224.0.0.1")]
    [TestCase("ff02::1")]
    [TestCase("fc00::1")]
    [TestCase("fe80::abcd")]
    [TestCase("198.18.0.1")]
    [TestCase("::127.0.0.1")]
    [TestCase("64:ff9b::7f00:1")]
    [TestCase("2002:7f00:1::1")]
    [TestCase("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    public void IsPublic_WhenAddressIsNotOnThePublicInternet_ShouldBeFalse(string address)
    {
        PublicAddress.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();
    }

    [TestCase("8.8.8.8")]
    [TestCase("172.32.0.1")]
    [TestCase("100.128.0.1")]
    [TestCase("2606:4700:4700::1111")]
    [TestCase("2001:4860:4860::8888")]
    [TestCase("::ffff:8.8.8.8")]
    public void IsPublic_WhenAddressIsOnThePublicInternet_ShouldBeTrue(string address)
    {
        PublicAddress.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();
    }

    [Test]
    public async Task LookupAsync_WhenARedirectLeadsToAPrivateHost_ShouldRefuseAtThatHop()
    {
        _http.Redirect("https://example.test/bounce", "https://inside.test/admin");
        var lookup = new HttpSourceLookup(_http, new FixedHostResolver(Public, new() { ["inside.test"] = "192.168.0.9" }));

        await Should.ThrowAsync<ApiException>(
            async () => await lookup.LookupAsync(SourceLookupKinds.Url, "https://example.test/bounce", CancellationToken.None));

        _http.Requests.ShouldBe(["https://example.test/bounce"]);
    }

    [Test]
    public async Task LookupAsync_WhenAJsonAnswerIsLargerThanTheLimit_ShouldRefuseIt()
    {
        var huge = "{\"ISBN:9780140449136\":{\"title\":\"" + new string('x', HttpSourceLookup.MaxJsonBytes) + "\"}}";
        _http.Json($"{OpenLibrary}9780140449136&format=json&jscmd=data", huge);

        var error = await Should.ThrowAsync<ApiException>(
            async () => await Create().LookupAsync(SourceLookupKinds.Isbn, KnownIsbn, CancellationToken.None));

        error.Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    public async Task LookupAsync_WhenAPageIsLargerThanTheLimit_ShouldStillReadItsTitle()
    {
        _http.Html("https://example.test/big", "<title>Big page</title>" + new string('a', HttpSourceLookup.MaxPageBytes * 2));

        var result = await Create().LookupAsync(SourceLookupKinds.Url, "https://example.test/big", CancellationToken.None);

        result.Source.ShouldNotBeNull().Title.ShouldBe("Big page");
    }

    [Test]
    public async Task LookupAsync_WhenRedirectsNeverEnd_ShouldReturnNotFound()
    {
        _http.Redirect("https://example.test/loop", "https://example.test/loop");

        var result = await Create().LookupAsync(SourceLookupKinds.Url, "https://example.test/loop", CancellationToken.None);

        result.Outcome.ShouldBe(SourceLookupResult.NotFoundOutcome);
    }
}
