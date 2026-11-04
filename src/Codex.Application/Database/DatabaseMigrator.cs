using DbUp;

namespace Codex.Application.Database;

// Applies the embedded db/ scripts with DbUp. The journal (schemaversions) makes it idempotent:
// only scripts not yet applied run, so a new migration is picked up and nothing runs twice.
public static class DatabaseMigrator
{
    public static void Apply(string connectionString, bool includeSeed)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScripts(DatabaseScripts.Load(includeSeed))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database upgrade failed at {result.ErrorScript?.Name}: {result.Error?.Message}", result.Error);
        }
    }
}
