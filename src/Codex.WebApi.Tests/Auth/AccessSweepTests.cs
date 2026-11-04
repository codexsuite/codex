using System.Net;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.WebApi.Tests.Auth;

// The access rule's stranger half (#29, #80): user B, who can open none of JD's books, calls every route in AccessSweep
// with JD's ids and gets 404 not_found, exactly as for an id that does not exist. Endpoints_ShouldAllBeSwept fails when
// an /api route (minimal API or controller) is added without a row in AccessSweep (or in its NoBookIds), and when a row
// names a route that is no longer mapped. AccessSweepRoleTests covers the grantees.
[TestFixture]
public class AccessSweepTests : ApiFixture
{
    private static readonly Guid UserB = new("0199c0de-0000-7000-8000-0000000000b2");
    private SweepIds _ids = null!;

    protected override TestUser SignedInAs => TestUser.Of(UserB);

    public static IEnumerable<TestCaseData> Cases()
        => AccessSweep.Routes().Select(r => new TestCaseData(r).SetArgDisplayNames(r.Key));

    [OneTimeSetUp]
    public async Task SeedJdsRowsAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "b@example.test", "User B", id: UserB);
        _ids = await AccessSweep.InsertJdsRowsAsync(ConnectionString);
    }

    [Test]
    public void Endpoints_ShouldAllBeSwept()
    {
        var swept = AccessSweep.Routes().Select(r => r.Key).Concat(AccessSweep.NoBookIds).ToHashSet();
        var endpoints = MappedApiRoutes();

        endpoints.Where(e => !swept.Contains(e)).ShouldBeEmpty();
        swept.Where(s => !endpoints.Contains(s)).ShouldBeEmpty();
    }

    // "GET /api/..." for every mapped /api route. A controller's attribute route has no leading slash
    // ("api/ai/integrations"), so it is added here; otherwise a controller route would drop out of the sweep unnoticed.
    private HashSet<string> MappedApiRoutes()
        => Host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => (Endpoint: e, Pattern: "/" + e.RoutePattern.RawText?.TrimStart('/').TrimEnd('/')))
            .Where(e => e.Pattern.StartsWith("/api/", StringComparison.Ordinal) && !e.Pattern.Contains("**"))
            .SelectMany(e => (e.Endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => $"{m} {e.Pattern}"))
            .ToHashSet();

    [TestCaseSource(nameof(Cases))]
    public async Task Request_WhenTheItemIsAnotherUsers_ShouldBeNotFound(SweepRoute route)
    {
        using var response = await AccessSweep.SendAsync(Client, route, _ids);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
