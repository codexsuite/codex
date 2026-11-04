using Codex.TestSupport.Database;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests;

// A scenario on the bare schema (no seed) with the one user the app acts as. Insert rows with TestRows.
public abstract class SchemaApiFixture : ApiFixture
{
    protected override Task<ClonedDatabase> CloneDatabaseAsync() => CodexDatabase.CloneSchemaAsync();

    [OneTimeSetUp]
    public async Task InsertUserAsync()
    {
        await using var connection = await OpenDatabaseAsync();
        await TestRows.EnsureUserAsync(connection);
    }
}
