using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Sharing;

// Sharing adds access for the people a book is shared with and nobody else (#80): a user with no grant still gets
// 404 not_found for JD's content, read or write, exactly as for an id that does not exist.
[TestFixture]
public class ContentAccessStrangerTests : ApiFixture
{
    private static readonly Guid Stranger = new("0199c0de-0000-7000-8000-0000000000e2");

    protected override TestUser SignedInAs => TestUser.Of(Stranger);

    [OneTimeSetUp]
    public Task CreateStrangerAsync() => TestUsers.CreateAsync(ConnectionString, "content-stranger@example.test", "Stranger", id: Stranger);

    private static IEnumerable<TestCaseData> Routes()
    {
        var book = SeedIds.Books.ProductEngineering;
        var shared = SeedIds.Books.PlayersGuide;
        yield return new TestCaseData("GET", $"/api/books/{book}/manuscript").SetArgDisplayNames("manuscript");
        yield return new TestCaseData("GET", $"/api/scenes/{SeedIds.Scenes.Ch3Sc1}/prose").SetArgDisplayNames("prose");
        yield return new TestCaseData("PUT", $"/api/scenes/{SeedIds.Scenes.PlayersGuideFigureScene}/prose").SetArgDisplayNames("prose save");
        yield return new TestCaseData("GET", $"/api/kb/entries/{SeedIds.KbEntries.Gravetouch}").SetArgDisplayNames("series kb entry");
        yield return new TestCaseData("PATCH", $"/api/kb/entries/{SeedIds.KbEntries.ConwaysLaw}").SetArgDisplayNames("kb entry edit");
        yield return new TestCaseData("GET", $"/api/references/{SeedIds.References.BeckXp}").SetArgDisplayNames("source");
        yield return new TestCaseData("PATCH", $"/api/references/{SeedIds.References.BeckXp}").SetArgDisplayNames("reference edit");
        yield return new TestCaseData("GET", $"/api/assets/{SeedIds.Assets.PlayersGuideFigure}/info").SetArgDisplayNames("image");
        yield return new TestCaseData("POST", $"/api/books/{shared}/exports").SetArgDisplayNames("export");
        yield return new TestCaseData("POST", $"/api/scenes/{SeedIds.Scenes.PlayersGuideFigureScene}/continue").SetArgDisplayNames("draft");
    }

    [TestCaseSource(nameof(Routes))]
    public async Task Call_WhenTheBookIsNotSharedWithTheUser_ShouldReturn404NotFound(string method, string url)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method != "GET")
        {
            request.Content = JsonContent.Create(new { });
        }

        using var response = await Client.SendAsync(request);

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
