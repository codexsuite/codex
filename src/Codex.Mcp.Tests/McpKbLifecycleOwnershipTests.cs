using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Hosting;

namespace Codex.Mcp.Tests;

// delete_kb_entry and merge_kb_entries act on JD's Knowledge base only: user B, whose library is empty, is handed JD's entries
// and gets not_found, by id and by name, exactly as for an id that does not exist (#29, #78).
[TestFixture]
[CancelAfter(60000)]
public class McpKbLifecycleOwnershipTests
{
    private const string UserBEmail = "b-kb@example.test";
    private McpStdioServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsUserAsync(async database =>
    {
        await TestUsers.CreateAsync(database.ConnectionString, UserBEmail, "User B");
        return UserBEmail;
    });

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [TestCase(false)]
    [TestCase(true)]
    public async Task DeleteKbEntry_WhenGivenAnotherUsersEntry_ShouldFailNotFound(bool dryRun)
    {
        var byId = await _server.Client.ErrorAsync("delete_kb_entry", new { entry = SeedIds.KbEntries.GoodhartsLaw.ToString(), dry_run = dryRun });
        var byName = await _server.Client.ErrorAsync("delete_kb_entry", new { entry = "Goodhart's Law", dry_run = dryRun });

        byId.GetProperty("code").GetString().ShouldBe("not_found");
        byName.GetProperty("code").GetString().ShouldBe("not_found");
    }

    [Test]
    public async Task MergeKbEntries_WhenGivenAnotherUsersEntries_ShouldFailNotFound()
    {
        var error = await _server.Client.ErrorAsync("merge_kb_entries",
            new { entry = SeedIds.KbEntries.GoodhartsLaw.ToString(), into = SeedIds.KbEntries.Gravetouch.ToString() });

        error.GetProperty("code").GetString().ShouldBe("not_found");
    }
}
