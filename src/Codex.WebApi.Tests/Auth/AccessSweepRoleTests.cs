using System.Net;
using Codex.Contracts;
using Codex.TestSupport.Fixtures;
using Codex.TestSupport.Hosting;
using Dapper;

namespace Codex.WebApi.Tests.Auth;

// The access rule's grantee half (#80): a user with one role on Product Engineering and on the Forsaken Gods series
// calls every route in AccessSweep with JD's ids. A route that needs no more than that role must not refuse them: not
// 403, and not 404 unless JD's own identical call is 404 too (the item may be missing for reasons of its own, such as
// a revision that does not exist or one this sweep's earlier call deleted). A route that needs more answers 403
// forbidden; a user's own things (Own) answer 404. Each role is its own fixture on its own database. Reads run first,
// so the editor's writes (archiving a scene, deleting a label) come after every read of them.
[TestFixture("view")]
[TestFixture("comment")]
[TestFixture("write")]
public class AccessSweepRoleTests(string role) : ApiFixture
{
    private readonly Guid _user = Guid.NewGuid();
    private SweepIds _ids = null!;
    private HttpClient _jd = null!;

    protected override TestUser SignedInAs => TestUser.Of(_user);

    private SweepNeeds Has => role switch
    {
        "view" => SweepNeeds.View,
        "comment" => SweepNeeds.Comment,
        _ => SweepNeeds.Write,
    };

    [OneTimeSetUp]
    public async Task GrantAsync()
    {
        await TestUsers.CreateAsync(ConnectionString, $"{role}-{_user:N}@example.test", $"Sweep {role}", id: _user);
        _ids = await AccessSweep.InsertJdsRowsAsync(ConnectionString);
        await using var db = await OpenDatabaseAsync();
        await TestRows.GrantAsync(db, SeedIds.Books.ProductEngineering, _user, role);
        await TestRows.GrantAsync(db, SeedIds.Series.ForsakenGods, _user, role);
        _jd = Host.CreateClient().ActingAs(SeedIds.Users.Jd);
    }

    [OneTimeTearDown]
    public void DisposeClient() => _jd.Dispose();

    [Test]
    public async Task Request_WhenTheUserHasTheRole_ShouldBeAllowedOrForbiddenByTheRoutesNeed()
    {
        var failures = new List<string>();
        foreach (var route in AccessSweep.Routes().OrderBy(r => r.Method != "GET"))
        {
            using var response = await AccessSweep.SendAsync(Client, route, _ids);
            var status = response.StatusCode;
            var failure = route.Needs switch
            {
                SweepNeeds.Own => status == HttpStatusCode.NotFound ? null : $"expected 404 (not the user's), got {(int)status}",
                _ when route.Needs > Has => status == HttpStatusCode.Forbidden ? null : $"expected 403 (needs {route.Needs}), got {(int)status}",
                _ when status == HttpStatusCode.Forbidden => "refused with 403, but the role is enough",
                _ when status == HttpStatusCode.NotFound && await JdGetsAsync(route) != HttpStatusCode.NotFound
                    => "404 for the grantee, but not for JD",
                _ => null,
            };
            if (failure is not null)
            {
                failures.Add($"{route.Key}: {failure}");
            }
        }

        failures.ShouldBeEmpty();
    }

    private async Task<HttpStatusCode> JdGetsAsync(SweepRoute route)
    {
        using var response = await AccessSweep.SendAsync(_jd, route, _ids);
        return response.StatusCode;
    }
}
