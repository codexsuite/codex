using Codex.TestSupport.Database;
using Codex.TestSupport.Fixtures;

namespace Codex.AcceptanceTests.Harness;

// Base class for a scenario on the large book: the seed plus db/seed-large, where Iron Litany is ~125,000 words in
// 3 acts, 30 chapters and 90 scenes. The class gets its own clone of codex_seed_large_<hash>; the template is built once.
public abstract class SeededLargeAcceptanceTest : AcceptanceFixture
{
    protected string DatabaseName { get; private set; } = null!;

    // This scenario's cloned database, for reading rows back.
    protected ClonedDatabase Clone { get; private set; } = null!;

    protected override async Task<ClonedDatabase> CloneDatabaseAsync()
    {
        var database = await CodexDatabase.CloneSeedLargeAsync();
        Clone = database;
        DatabaseName = database.Name;
        return database;
    }
}
