using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Sharing;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;

namespace Codex.WebApi.Tests.Sharing;

// GET /api/books/{id}/sharing on the seed: Ana views Product Engineering and edits the Forsaken Gods series. Everyone
// who can open the book sees the whole list; only the owner sees email addresses.
[TestFixture]
public class SharingReadTests : ApiFixture
{
    private static readonly Guid Stranger = new("0199c0de-0000-7000-8000-0000000000c4");

    [OneTimeSetUp]
    public Task AddStrangerAsync() => TestUsers.CreateAsync(ConnectionString, "stranger4@example.test", "Stranger", id: Stranger);

    [Test]
    public async Task Get_WhenTheOwnerAsks_ShouldListOwnerThenGrantsWithEmails()
    {
        var sharing = await Client.GetFromJsonAsync<SharingDto>($"/api/books/{SeedIds.Books.ProductEngineering}/sharing");

        sharing!.Owner.ShouldBe(new SharingUserDto(SeedIds.Users.Jd, "Joel Dickson", "jd@codex.local"));
        sharing.Role.ShouldBe("owner");
        sharing.Book.ShouldBe([new SharingGrantDto(new SharingUserDto(SeedIds.Users.Ana, "Ana Kowalczyk", "ana@codex.local"), "view")]);
        sharing.Series.ShouldBeNull();
    }

    [Test]
    public async Task Get_WhenACollaboratorAsks_ShouldListEveryoneWithoutEmails()
    {
        using var ana = Host.CreateClient().ActingAs(SeedIds.Users.Ana);

        var sharing = await ana.GetFromJsonAsync<SharingDto>($"/api/books/{SeedIds.Books.PlayersGuide}/sharing");

        sharing!.Owner.ShouldBe(new SharingUserDto(SeedIds.Users.Jd, "Joel Dickson", null));
        sharing.Role.ShouldBe("write");
        sharing.Book.ShouldBeEmpty();
        sharing.Series.ShouldNotBeNull();
        sharing.Series.Id.ShouldBe(SeedIds.Series.ForsakenGods);
        sharing.Series.Title.ShouldBe("Forsaken Gods");
        sharing.Series.Grants.ShouldBe([new SharingGrantDto(new SharingUserDto(SeedIds.Users.Ana, "Ana Kowalczyk", null), "write")]);
    }

    [Test]
    public async Task Get_WhenTheCallerCannotOpenTheBook_ShouldBeNotFound()
    {
        using var stranger = Host.CreateClient().ActingAs(Stranger);

        var response = await stranger.GetAsync($"/api/books/{SeedIds.Books.ProductEngineering}/sharing");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Get_WhenTheIdIsASeries_ShouldBeNotFound()
    {
        var response = await Client.GetAsync($"/api/books/{SeedIds.Series.ForsakenGods}/sharing");

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }
}
