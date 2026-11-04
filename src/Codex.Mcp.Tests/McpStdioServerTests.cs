using Codex.Mcp.Tests.Support;
using Codex.TestSupport.Database;
using Dapper;
using Npgsql;

namespace Codex.Mcp.Tests;

// The test harness itself: a server process that never answers must not leave its database clone behind.
[TestFixture]
[CancelAfter(90000)]
public class McpStdioServerTests
{
    [Test]
    public async Task StartAsync_WhenTheServerProcessFails_ShouldThrowAndDropTheClone()
    {
        ClonedDatabase? clone = null;

        await Should.ThrowAsync<Exception>(() =>
            McpStdioServer.StartAsync(Path.Combine(AppContext.BaseDirectory, "no-such-server.dll"), database => clone = database));

        clone.ShouldNotBeNull();
        var server = new NpgsqlConnectionStringBuilder(clone.ConnectionString) { Database = "postgres", Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(server);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_database WHERE datname = @name", new { name = clone.Name })).ShouldBe(0);
    }
}
