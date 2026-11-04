using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Sharing;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;

namespace Codex.WebApi.Tests.Sharing;

// The lookup's limit is per user: 20 a minute (and 200 a day). JD's 21st call in the minute is refused with 429
// rate_limited; another owner's calls are counted apart.
[TestFixture]
public class SharingLookupRateLimitTests : ApiFixture
{
    private static readonly Guid OtherOwner = new("0199c0de-0000-7000-8000-0000000000c3");
    private Guid _otherBook;

    [OneTimeSetUp]
    public async Task AddAnotherOwnerAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "owner2@example.test", "Second Owner", id: OtherOwner);
        await using var db = await OpenDatabaseAsync();
        _otherBook = await db.ExecuteScalarAsync<Guid>(
            "INSERT INTO book (owner_id, title) VALUES (@OtherOwner, 'Second book') RETURNING id", new { OtherOwner });
    }

    [Test]
    [Order(1)]
    public async Task Lookup_WhenTheTwentyFirstCallInAMinute_ShouldBeRateLimited()
    {
        var url = $"/api/books/{SeedIds.Books.ProductEngineering}/sharing/lookup";
        for (var i = 0; i < 20; i++)
        {
            var allowed = await Client.PostAsJsonAsync(url, new SharingLookupRequest(i % 2 == 0 ? "ana@codex.local" : $"nobody{i}@example.test"));
            allowed.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
        }

        var refused = await Client.PostAsJsonAsync(url, new SharingLookupRequest("ana@codex.local"));

        (await refused.ReadApiErrorAsync(HttpStatusCode.TooManyRequests)).Code.ShouldBe(ApiErrorCodes.RateLimited);
    }

    [Test]
    [Order(2)]
    public async Task Lookup_WhenAnotherUserIsLimited_ShouldStillAnswer()
    {
        using var other = Host.CreateClient().ActingAs(OtherOwner);

        var response = await other.PostAsJsonAsync($"/api/books/{_otherBook}/sharing/lookup", new SharingLookupRequest("ana@codex.local"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
