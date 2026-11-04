using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.References;

namespace Codex.WebApi.Tests.References;

// The real provider (no fake): an address on a private network is refused before anything is sent.
[TestFixture]
public class AddSourceRefusalTests : SourceApiFixture
{
    [TestCase("http://127.0.0.1:9/secret")]
    [TestCase("http://169.254.169.254/latest/meta-data/")]
    [TestCase("http://[::1]/admin")]
    [TestCase("http://192.168.1.1/")]
    public async Task Lookup_WhenTheUrlIsOnAPrivateNetwork_ShouldBe400Validation(string url)
    {
        using var response = await Client.PostAsJsonAsync(
            $"/api/books/{SeedIds.Books.ProductEngineering}/references/lookup", new LookupSourceRequest("url", url));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe(ApiErrorCodes.Validation);
        error.Error.ShouldContain("public internet");
    }
}
