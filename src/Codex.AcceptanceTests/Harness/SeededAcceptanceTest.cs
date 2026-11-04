using Codex.TestSupport.Database;
using Codex.TestSupport.Fixtures;

namespace Codex.AcceptanceTests.Harness;

// Base class for a scenario that starts from the seed (the Library with its books, the knowledge base, ...).
// The class gets its own cloned copy of codex_seed_<hash> and its own app instance; tests inside run in
// [Order]. Put a lane's class under Lanes/<Lane>/ in namespace Codex.AcceptanceTests.Lanes.<Lane>.
public abstract class SeededAcceptanceTest : AcceptanceFixture
{
    protected string DatabaseName { get; private set; } = null!;

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        var database = await CodexDatabase.CloneSeedAsync();
        DatabaseName = database.Name;
        return database;
    }
}
