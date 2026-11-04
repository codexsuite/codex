using Codex.TestSupport.Database;
using Codex.TestSupport.Fixtures;

namespace Codex.AcceptanceTests.Harness;

// Base class for a scenario that starts from the bare schema (no seed rows): the first-run experience,
// or a test that builds exactly the data it needs. Same isolation as SeededAcceptanceTest.
public abstract class EmptyAcceptanceTest : AcceptanceFixture
{
    protected string DatabaseName { get; private set; } = null!;

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        var database = await CodexDatabase.CloneSchemaAsync();
        DatabaseName = database.Name;
        return database;
    }
}
