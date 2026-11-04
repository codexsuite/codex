using Codex.Contracts;
using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Hosting;
using Dapper;
using Npgsql;

namespace Codex.Mcp.Tests;

// update_source_use as user B, handed a use in JD's book (#29): it answers not_found, as for an id that does not exist, and
// nothing of JD's changes.
[TestFixture]
[CancelAfter(60000)]
public class McpUpdateSourceUseOwnershipTests
{
    private const string UserBEmail = "b@example.test";
    private McpStdioServer _server = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _server = await McpStdioServer.StartAsUserAsync(async database =>
    {
        await TestUsers.CreateAsync(database.ConnectionString, UserBEmail, "User B");
        return UserBEmail;
    });

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    [Test]
    public async Task UpdateSourceUse_WhenTheUseIsInAnotherUsersBook_ShouldBeNotFoundAndChangeNothing()
    {
        var use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;

        var error = await _server.Client.ErrorAsync("update_source_use", new { use, kind = "paraphrase" });
        var removed = await _server.Client.ErrorAsync("update_source_use", new { use, remove = true });
        var wrapped = await _server.Client.ErrorAsync("update_source_use", new { use, rewrap = new { text = "x" }, expected_version = 1 });

        error.GetProperty("code").GetString().ShouldBe("not_found");
        removed.GetProperty("code").GetString().ShouldBe("not_found");
        wrapped.GetProperty("code").GetString().ShouldBe("not_found");
        await using var connection = new NpgsqlConnection(_server.ConnectionString);
        (await connection.ExecuteScalarAsync<string>("SELECT kind FROM reference_use WHERE id = @id", new { id = use })).ShouldBe("quote");
    }
}
