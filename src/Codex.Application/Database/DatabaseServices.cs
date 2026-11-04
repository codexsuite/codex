using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Codex.Application.Database;

// CODEX_DB_CONNECTION is the one connection string. CODEX_DB_MIGRATE (default true) applies
// db/schema and db/migrations when the app starts; the seed is never applied by the app.
public sealed class DatabaseServices : IServiceModule
{
    public const string ConnectionKey = "CODEX_DB_CONNECTION";
    public const string MigrateKey = "CODEX_DB_MIGRATE";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        DapperTypeHandlers.Register();
        services.AddSingleton(sp => NpgsqlDataSource.Create(ConnectionString(sp.GetRequiredService<IConfiguration>())));
        services.AddHostedService<DatabaseMigrationService>();
    }

    // Every session this app opens runs at UTC whatever PGTZ or the connection string says (utc-everywhere): the pin is
    // here, not in the environment, so web, worker, stdio MCP and tests all get it.
    public static string PinUtc(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Timezone = "UTC" }.ConnectionString;

    private static string ConnectionString(IConfiguration configuration)
        => PinUtc(configuration[ConnectionKey]
           ?? throw new InvalidOperationException($"{ConnectionKey} is not set."));

    private sealed class DatabaseMigrationService(IConfiguration configuration) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (configuration.GetValue(MigrateKey, true))
            {
                DatabaseMigrator.Apply(ConnectionString(configuration), includeSeed: false);
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
