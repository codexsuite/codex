using System.Net;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Auth;

// CSRF: a cookie-carrying write that a cross-site HTML form could send (no JSON body, no X-Codex-Client header) is
// refused with 403 before it reaches the endpoint.
[TestFixture]
public class CrossSiteWriteTests : ApiFixture
{
    protected override TestUser SignedInAs => TestUser.Anonymous;

    [Test]
    public async Task Post_WhenSignedInByCookieWithAFormBody_ShouldBe403()
    {
        using var client = await FormLikeClientAsync();

        var response = await client.PostAsync($"/api/imports/{Guid.NewGuid()}/dismiss",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("a", "b")]));

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
    }

    [Test]
    public async Task Post_WhenSignedInByCookieWithNoBody_ShouldBe403()
    {
        using var client = await FormLikeClientAsync();

        var response = await client.PostAsync($"/api/imports/{Guid.NewGuid()}/dismiss", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Post_WhenItCarriesTheClientHeader_ShouldReachTheEndpoint()
    {
        using var client = await FormLikeClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/imports/{Guid.NewGuid()}/dismiss");
        request.Headers.Add(AuthClient.ClientHeader, "tab-1");

        var response = await client.SendAsync(request);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    // Signed in by cookie, but sending only what an HTML form could: no X-Codex-Client header.
    private async Task<HttpClient> FormLikeClientAsync()
    {
        var client = await AuthClient.SignedInAsync(Host, TestUsers.JdEmail, TestUsers.DevPassword);
        client.DefaultRequestHeaders.Remove(AuthClient.ClientHeader);
        return client;
    }
}
