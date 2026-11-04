using Npgsql;
using Testcontainers.PostgreSql;

namespace Codex.TestSupport.Database;

// The Postgres server every test class shares. CODEX_TEST_DB (a connection string to a long-lived
// server) wins; otherwise one postgres:18 container is started and reused across runs.
internal static class PostgresServer
{
    private static readonly Lazy<Task<string>> Admin = new(StartAsync);

    // A connection string to the server's maintenance database, unpooled.
    public static Task<string> AdminConnectionStringAsync() => Admin.Value;

    private static async Task<string> StartAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_TEST_DB");
        string connectionString;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            connectionString = configured;
        }
        else
        {
            var container = new PostgreSqlBuilder("postgres:18")
                .WithReuse(true)
                .WithLabel("codex-tests", "postgres-18")
                .Build();
            await container.StartAsync();
            connectionString = container.GetConnectionString();
        }
        return new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false, Timezone = "UTC" }.ConnectionString;
    }
}
