using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Sharing;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;

namespace Codex.WebApi.Tests.Sharing;

// POST /api/books/{id}/sharing/lookup: one exact address, case and surrounding whitespace aside, and only for the
// owner. JD owns Product Engineering; seeded Ana can view it. Fewer than 20 of JD's calls, so the limit stays out of it.
[TestFixture]
public class SharingLookupTests : ApiFixture
{
    private static readonly Guid Disabled = new("0199c0de-0000-7000-8000-0000000000c1");
    private static readonly Guid Stranger = new("0199c0de-0000-7000-8000-0000000000c2");

    private static string Url => $"/api/books/{SeedIds.Books.ProductEngineering}/sharing/lookup";

    [OneTimeSetUp]
    public async Task AddUsersAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "gone@example.test", "Gone User", id: Disabled);
        await TestUsers.CreateAsync(ConnectionString, "stranger@example.test", "Stranger", id: Stranger);
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE app_user SET disabled_at = now() WHERE id = @Disabled", new { Disabled });
    }

    [TestCase("ana@codex.local")]
    [TestCase("ANA@Codex.Local")]
    [TestCase("  ana@codex.local\t")]
    public async Task Lookup_WhenTheFullAddressInAnyCase_ShouldFindTheUser(string email)
    {
        var response = await Client.PostAsJsonAsync(Url, new SharingLookupRequest(email));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SharingUserDto>())
            .ShouldBe(new SharingUserDto(SeedIds.Users.Ana, "Ana Kowalczyk", "ana@codex.local"));
    }

    [TestCase("ana")]
    [TestCase("ana@codex")]
    [TestCase("ana@codex.co")]
    [TestCase("@codex.local")]
    [TestCase("codex.local")]
    [TestCase("na@codex.local")]
    [TestCase("ana@codex.local.")]
    public async Task Lookup_WhenOnlyPartOfAnAddress_ShouldBeNotFound(string email)
    {
        var response = await Client.PostAsJsonAsync(Url, new SharingLookupRequest(email));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Lookup_WhenTheUserIsDisabled_ShouldBeNotFound()
    {
        var response = await Client.PostAsJsonAsync(Url, new SharingLookupRequest("gone@example.test"));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Lookup_WhenTheCallerOnlyViewsTheBook_ShouldBeForbidden()
    {
        using var ana = Host.CreateClient().ActingAs(SeedIds.Users.Ana);

        var response = await ana.PostAsJsonAsync(Url, new SharingLookupRequest("jd@codex.local"));

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
    }

    [Test]
    public async Task Lookup_WhenTheCallerCannotOpenTheBook_ShouldBeNotFound()
    {
        using var stranger = Host.CreateClient().ActingAs(Stranger);

        var response = await stranger.PostAsJsonAsync(Url, new SharingLookupRequest("ana@codex.local"));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task Lookup_WhenTheEmailIsBlank_ShouldBeAValidationError()
    {
        var response = await Client.PostAsJsonAsync(Url, new SharingLookupRequest("   "));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }
}
