using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.WebApi.Tests;

// utc-everywhere: the app pins its Postgres sessions to UTC itself, so PGTZ or a zone in the connection string cannot
// move a day boundary or an exported offset.
[TestFixture]
public class UtcSessionTests
{
    [Test]
    public async Task DataSource_WhenTheConnectionStringNamesAnotherZone_ShouldStillRunSessionsAtUtc()
    {
        await using var clone = await CodexDatabase.CloneSchemaAsync();
        var bangkok = new NpgsqlConnectionStringBuilder(clone.ConnectionString) { Timezone = "Asia/Bangkok" }.ConnectionString;
        await using var host = CodexHost.Start(bangkok, TestUser.Jd);

        await using var connection = await host.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var show = new NpgsqlCommand("SHOW TimeZone", connection);

        ((string)(await show.ExecuteScalarAsync())!).ShouldBe("UTC");
    }
}
