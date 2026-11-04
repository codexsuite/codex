using System.Net;
using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Sharing;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;

namespace Codex.WebApi.Tests.Sharing;

// PUT and DELETE /api/books/{id}/sharing/{userId} on the seed, in order: JD shares The Quiet Algorithm with Bo, changes
// the role, shares the Forsaken Gods series, and removes grants. The refusals come first and change nothing.
[TestFixture]
public class SharingGrantTests : ApiFixture
{
    private static readonly Guid Bo = new("0199c0de-0000-7000-8000-0000000000c5");
    private static readonly Guid Disabled = new("0199c0de-0000-7000-8000-0000000000c6");

    private sealed record GrantRow(string Role, Guid GrantedBy, bool Touched);

    private static string Url(Guid id, Guid user) => $"/api/books/{id}/sharing/{user}";

    private static Guid Book => SeedIds.Books.TheQuietAlgorithm;

    [OneTimeSetUp]
    public async Task AddUsersAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, "bo@example.test", "Bo Lindqvist", id: Bo);
        await TestUsers.CreateAsync(ConnectionString, "off@example.test", "Off User", id: Disabled);
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync("UPDATE app_user SET disabled_at = now() WHERE id = @Disabled", new { Disabled });
    }

    [Test]
    [Order(1)]
    public async Task Put_WhenTheUserIsTheOwner_ShouldBeAConflict()
    {
        var response = await Client.PutAsJsonAsync(Url(Book, SeedIds.Users.Jd), new SetGrantRequest("view"));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [TestCase("owner")]
    [TestCase("admin")]
    [TestCase("View")]
    [Order(2)]
    public async Task Put_WhenTheRoleIsNotAGrantRole_ShouldBeAValidationError(string role)
    {
        var response = await Client.PutAsJsonAsync(Url(Book, Bo), new SetGrantRequest(role));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
    }

    [Test]
    [Order(3)]
    public async Task Put_WhenTheUserIsUnknownOrDisabled_ShouldBeNotFound()
    {
        var unknown = await Client.PutAsJsonAsync(Url(Book, Guid.NewGuid()), new SetGrantRequest("view"));
        var disabled = await Client.PutAsJsonAsync(Url(Book, Disabled), new SetGrantRequest("view"));

        (await unknown.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        (await disabled.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(4)]
    public async Task Put_WhenTheCallerOnlyEdits_ShouldBeForbidden()
    {
        using var ana = Host.CreateClient().ActingAs(SeedIds.Users.Ana);

        var response = await ana.PutAsJsonAsync(Url(SeedIds.Books.PlayersGuide, Bo), new SetGrantRequest("view"));

        (await response.ReadApiErrorAsync(HttpStatusCode.Forbidden)).Code.ShouldBe(ApiErrorCodes.Forbidden);
    }

    [Test]
    [Order(5)]
    public async Task Put_WhenNew_ShouldAddTheGrantAndListIt()
    {
        var response = await Client.PutAsJsonAsync(Url(Book, Bo), new SetGrantRequest("view"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<SharingGrantDto>())
            .ShouldBe(new SharingGrantDto(new SharingUserDto(Bo, "Bo Lindqvist", "bo@example.test"), "view"));
        var sharing = await Client.GetFromJsonAsync<SharingDto>($"/api/books/{Book}/sharing");
        sharing!.Book.Select(g => (g.User.Id, g.Role)).ShouldBe([(Bo, "view")]);
        using var bo = Host.CreateClient().ActingAs(Bo);
        (await bo.GetAsync($"/api/books/{Book}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    [Order(6)]
    public async Task Put_WhenTheGrantExists_ShouldChangeItsRoleAndStampIt()
    {
        await using var db = await OpenDatabaseAsync();
        await db.ExecuteAsync(
            "UPDATE book_grant SET updated_at = now() - interval '1 day', granted_by = @Bo WHERE book_id = @Book AND user_id = @Bo",
            new { Book, Bo });

        var response = await Client.PutAsJsonAsync(Url(Book, Bo), new SetGrantRequest("write"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await db.QuerySingleAsync<GrantRow>(
            """
            SELECT role, granted_by AS GrantedBy, updated_at > now() - interval '1 hour' AS Touched
            FROM book_grant WHERE book_id = @Book AND user_id = @Bo
            """, new { Book, Bo })).ShouldBe(new GrantRow("write", SeedIds.Users.Jd, true));
    }

    [Test]
    [Order(7)]
    public async Task Put_WhenTheIdIsTheSeries_ShouldListItUnderTheSeries()
    {
        var response = await Client.PutAsJsonAsync(Url(SeedIds.Series.ForsakenGods, Bo), new SetGrantRequest("comment"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sharing = await Client.GetFromJsonAsync<SharingDto>($"/api/books/{SeedIds.Books.BestiaryOfTheFallen}/sharing");
        sharing!.Book.ShouldBeEmpty();
        sharing.Series!.Grants.Select(g => (g.User.Id, g.Role)).ShouldBe([(SeedIds.Users.Ana, "write"), (Bo, "comment")]);
    }

    [Test]
    [Order(8)]
    public async Task Delete_WhenTheUserHasNoGrantThere_ShouldBeNotFound()
    {
        var response = await Client.DeleteAsync(Url(SeedIds.Books.PlayersGuide, Bo));

        (await response.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test]
    [Order(9)]
    public async Task Delete_WhenTheGrantExists_ShouldRemoveIt()
    {
        var response = await Client.DeleteAsync(Url(Book, Bo));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var bo = Host.CreateClient().ActingAs(Bo);
        (await (await bo.GetAsync($"/api/books/{Book}")).ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
        (await bo.GetAsync($"/api/books/{SeedIds.Books.PlayersGuide}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<bool>(
            "SELECT archived_at IS NOT NULL FROM book_grant WHERE book_id = @Book AND user_id = @Bo", new { Book, Bo })).ShouldBeTrue();
        (await Client.DeleteAsync(Url(Book, Bo))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    [Order(10)]
    public async Task Put_WhenTheGrantWasRemoved_ShouldReviveItAndRestoreAccess()
    {
        var response = await Client.PutAsJsonAsync(Url(Book, Bo), new SetGrantRequest("comment"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var bo = Host.CreateClient().ActingAs(Bo);
        (await bo.GetAsync($"/api/books/{Book}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM book_grant WHERE book_id = @Book AND user_id = @Bo AND archived_at IS NULL AND role = 'comment'",
            new { Book, Bo })).ShouldBe(1);
        (await Client.GetFromJsonAsync<SharingDto>($"/api/books/{Book}/sharing"))!.Book.Select(g => (g.User.Id, g.Role)).ShouldBe([(Bo, "comment")]);
    }
}
