using System.Net;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Codex.WebApi.Tests.Shell;
using Dapper;

namespace Codex.WebApi.Tests.Sharing;

// Archiving a book deletes its own grants (#80); grants on its series stay, so a series collaborator keeps the book.
// Seed: Ana views Product Engineering (a grant on the book) and edits the Forsaken Gods series (Players Guide).
[TestFixture]
public class SharingArchiveTests : ApiFixture
{
    private HttpClient _ana = null!;

    [OneTimeSetUp]
    public void CreateClient() => _ana = Host.CreateClient().ActingAs(SeedIds.Users.Ana);

    [OneTimeTearDown]
    public void DisposeClient() => _ana.Dispose();

    [Test]
    [Order(1)]
    public async Task Archive_WhenTheBookIsSharedDirectly_ShouldArchiveItsGrants()
    {
        (await Client.PostAsync($"/api/books/{SeedIds.Books.ProductEngineering}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM book_grant WHERE book_id = @id AND archived_at IS NULL", new { id = SeedIds.Books.ProductEngineering })).ShouldBe(0);
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM book_grant WHERE book_id = @id AND archived_at IS NOT NULL", new { id = SeedIds.Books.ProductEngineering })).ShouldBeGreaterThan(0);
        (await (await _ana.GetAsync($"/api/books/{SeedIds.Books.ProductEngineering}")).ReadApiErrorAsync(HttpStatusCode.NotFound))
            .Code.ShouldBe(ApiErrorCodes.NotFound);
        (await _ana.GetLibraryAsync()).Standalone.ShouldNotContain(b => b.Id == SeedIds.Books.ProductEngineering);
    }

    [Test]
    [Order(2)]
    public async Task Restore_WhenTheBookHadGrants_ShouldNotBringThemBack()
    {
        (await Client.PostAsync($"/api/books/{SeedIds.Books.ProductEngineering}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _ana.GetAsync($"/api/books/{SeedIds.Books.ProductEngineering}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    [Order(3)]
    public async Task Archive_WhenTheBookIsSharedThroughItsSeries_ShouldKeepTheSeriesGrant()
    {
        (await Client.PostAsync($"/api/books/{SeedIds.Books.PlayersGuide}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _ana.GetAsync($"/api/books/{SeedIds.Books.PlayersGuide}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _ana.GetLibraryAsync()).Series.SelectMany(s => s.Books).ShouldNotContain(b => b.Id == SeedIds.Books.PlayersGuide);
        (await _ana.GetLibraryAsync("?status=archived")).Series.SelectMany(s => s.Books).ShouldBeEmpty();
    }

    [Test]
    [Order(4)]
    public async Task Archive_WhenACollaboratorEditsTheBook_ShouldBeForbidden()
    {
        var response = await _ana.PostAsync($"/api/books/{SeedIds.Books.GamemastersCodex}/archive", null);

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
    }
}
