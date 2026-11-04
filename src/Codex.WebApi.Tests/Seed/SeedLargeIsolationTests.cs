using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// db/seed-large is applied by the codex_seed_large template only: the normal seed template (and so the app's
// own schema-only database) must not see it.
[TestFixture]
public class SeedLargeIsolationTests
{
    private ClonedDatabase _database = null!;

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    [Test]
    public async Task NormalSeed_ShouldKeepIronLitanyAtItsFillerSize()
    {
        await using var connection = await _database.OpenAsync();

        var row = await connection.QuerySingleAsync<(int Words, int Chapters, int Scenes)>(
            """
            SELECT s.word_count,
                   (SELECT count(*) FROM chapter_outline c WHERE c.book_id = b.id)::int,
                   (SELECT count(*) FROM scene x WHERE x.book_id = b.id AND x.archived_at IS NULL)::int
            FROM book_stats s JOIN book b ON b.id = s.book_id
            WHERE b.id = @id
            """, new { id = SeedIds.Books.IronLitany });

        row.ShouldBe((855, 3, 6));
    }

    [Test]
    public async Task NormalSeed_ShouldHaveNoAshenCrownSeriesEntries()
    {
        await using var connection = await _database.OpenAsync();

        var entries = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM kb_entry WHERE name IN ('Maren Voss', 'Orrin Hale', 'Sister Ysolde', 'Cinder Gate', 'The Iron Choir', 'Hollow Ridge')");

        entries.ShouldBe(0);
    }
}
