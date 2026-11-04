using Shouldly;
using Codex.Contracts.Sources;

namespace Codex.TestSupport.Contracts;

// Every ISourceLookup implementation inherits this. The known values default to the ones in
// Fixtures/source-lookup.json; a real implementation's fixture overrides them with values its backend knows.
public abstract class SourceLookupContract
{
    protected virtual string KnownIsbn => "9780140449136";

    protected virtual string KnownDoi => "10.1000/xyz123";

    protected virtual string RedirectingUrl => "https://example.test/old-address";

    protected virtual string UnknownDoi => "10.9999/nobody-knows";

    protected abstract ISourceLookup Create();

    [Test]
    public async Task LookupAsync_WhenIsbnIsKnown_ShouldReturnFoundWithTitleAndAuthors()
    {
        var result = await Create().LookupAsync(SourceLookupKinds.Isbn, KnownIsbn, CancellationToken.None);

        result.Outcome.ShouldBe(SourceLookupResult.FoundOutcome);
        var source = result.Source.ShouldNotBeNull();
        source.Title.ShouldNotBeNullOrWhiteSpace();
        source.Authors.ShouldNotBeEmpty();
    }

    [Test]
    public async Task LookupAsync_WhenDoiIsKnown_ShouldReturnFoundWithYear()
    {
        var result = await Create().LookupAsync(SourceLookupKinds.Doi, KnownDoi, CancellationToken.None);

        result.Outcome.ShouldBe(SourceLookupResult.FoundOutcome);
        result.Source.ShouldNotBeNull().Year.ShouldNotBeNull();
    }

    [Test]
    public async Task LookupAsync_WhenValueIsUnknown_ShouldReturnNotFound()
    {
        var result = await Create().LookupAsync(SourceLookupKinds.Doi, UnknownDoi, CancellationToken.None);

        result.Outcome.ShouldBe(SourceLookupResult.NotFoundOutcome);
        result.Source.ShouldBeNull();
    }

    [Test]
    public async Task LookupAsync_WhenUrlMoved_ShouldReturnRedirectToTheNewValue()
    {
        var result = await Create().LookupAsync(SourceLookupKinds.Url, RedirectingUrl, CancellationToken.None);

        result.Outcome.ShouldBe(SourceLookupResult.RedirectOutcome);
        result.RedirectTo.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task LookupAsync_WhenKindIsUnknown_ShouldThrowArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(
            async () => await Create().LookupAsync("pigeon", "x", CancellationToken.None));
    }
}
