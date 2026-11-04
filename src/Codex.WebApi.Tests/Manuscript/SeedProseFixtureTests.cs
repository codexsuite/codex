using System.Text.Json;
using Codex.TestSupport;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Manuscript;

// Pins Fixtures/manuscript/seed-prose.json (every live scene's prose body in the seed, keyed by scene id) to the seed.
// The client round-trips every body in that file through parse/serialize (features/write/seedProse.test.ts), so a
// seed change that alters prose fails here until the fixture is regenerated:
//   psql -At -d codex_seed_<hash> -c "select json_object_agg(p.scene_id, p.body) from prose p
//     join scene s on s.id = p.scene_id where s.archived_at is null" | jq -S . > src/Codex.TestSupport/Fixtures/manuscript/seed-prose.json
// (<hash> is the template database the test run created; list them with `psql -l`).
[TestFixture]
public class SeedProseFixtureTests
{
    private sealed record ProseRow(Guid SceneId, string Body);

    private const string Stale =
        "seed-prose.json no longer matches the seed's prose. Regenerate it with the psql command in the header of SeedProseFixtureTests.cs.";

    private ClonedDatabase _database = null!;

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    [Test]
    public async Task SeedProseFixture_WhenSeeded_ShouldEqualEveryLiveProseBody()
    {
        await using var connection = await _database.OpenAsync();
        var seeded = (await connection.QueryAsync<ProseRow>(
            """
            SELECT p.scene_id AS SceneId, p.body
            FROM prose p JOIN scene s ON s.id = p.scene_id
            WHERE s.archived_at IS NULL
            """)).ToDictionary(r => r.SceneId.ToString(), r => r.Body);
        var path = Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "manuscript", "seed-prose.json");
        var fixture = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path))!;

        fixture.Keys.Order().ShouldBe(seeded.Keys.Order(), Stale);
        fixture.Where(f => seeded[f.Key] != f.Value).Select(f => f.Key).ShouldBeEmpty(Stale);
    }
}
