using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// The embeds in the seeded prose (kb: mentions, ::kb-addition, ref: links, ::figure) must survive an ordinary save.
// One scenario on its own clone, because it writes.
[TestFixture]
public class SeedEmbedTests
{
    private ClonedDatabase _database = null!;

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    private const string Snapshot =
        """
        SELECT 'use' AS Kind, id::text AS Id, concat_ws('|', (archived_at IS NOT NULL)::text, excerpt, changed_at::text, generation_id::text) AS State FROM reference_use
        UNION ALL SELECT 'addition', id::text, (archived_at IS NOT NULL)::text FROM kb_progression
        UNION ALL SELECT 'beat', id::text, (archived_at IS NOT NULL)::text FROM beat
        UNION ALL SELECT 'figure', id::text, (archived_at IS NOT NULL)::text FROM figure
        UNION ALL SELECT 'status', reference_id::text, status || '|' || reason FROM reference_clearance
        ORDER BY 1, 2
        """;

    public sealed record Row(string Kind, string Id, string State);

    [Test]
    public async Task Embeds_WhenProseSavedUnchanged_ShouldKeepEveryUseAndAddition()
    {
        await using var connection = await _database.OpenAsync();
        var before = (await connection.QueryAsync<Row>(Snapshot)).ToList();

        var saved = await connection.ExecuteAsync(
            """
            UPDATE prose SET body = body
            WHERE scene_id IN (SELECT scene_id FROM reference_use UNION SELECT scene_id FROM kb_progression
                               UNION SELECT scene_id FROM figure UNION SELECT scene_id FROM beat)
            """);
        var after = (await connection.QueryAsync<Row>(Snapshot)).ToList();

        saved.ShouldBeGreaterThan(8);
        after.ShouldBe(before);
        before.Count(r => r.Kind == "use").ShouldBe(15);
        before.Where(r => r.Kind != "status").ShouldAllBe(r => r.Kind == "use" || r.State == "false");
        before.Where(r => r.Kind == "use").ShouldAllBe(r => r.State.StartsWith("false|"));
    }

    [Test]
    public async Task Embeds_ShouldAppearInTheProse()
    {
        await using var connection = await _database.OpenAsync();

        var linkedUses = (await connection.QueryAsync<Guid>(
            "SELECT id FROM reference_use u WHERE EXISTS (SELECT 1 FROM prose p WHERE p.scene_id = u.scene_id AND strpos(p.body, '(ref:' || u.id || ')') > 0)")).ToList();
        var draftUses = (await connection.QueryAsync<Guid>(
            "SELECT id FROM reference_use WHERE generation_id = @g", new { g = SeedIds.AiGenerations.Ch5Sc2Pending })).ToList();

        draftUses.ShouldBe(
            [SeedIds.ReferenceUses.GoodhartMonetaryCh5Sc2, SeedIds.ReferenceUses.DoraReport2024Ch5Sc2, SeedIds.ReferenceUses.AccelerateCh5Sc2],
            ignoreOrder: true);
        linkedUses.Count.ShouldBe(15 - draftUses.Count);
        (await connection.ExecuteScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.Ch4Sc2 }))
            .ShouldBe($"[^](ref:{SeedIds.ReferenceUses.ConwayCommitteesCh4Sc2})\n");

        var kbMentions = (await connection.QueryAsync<(Guid Scene, Guid Entry)>(
            "SELECT scene_id, (regexp_matches(body, '\\(kb:([0-9a-f-]{36})\\)', 'g'))[1]::uuid FROM prose")).ToList();
        kbMentions.ShouldBe(
        [
            (SeedIds.Scenes.Ch3Sc1, SeedIds.KbEntries.KentBeck), (SeedIds.Scenes.Ch5Sc1, SeedIds.KbEntries.MartyCagan),
            (SeedIds.Scenes.Ch5Sc1, SeedIds.KbEntries.ThePaymentsRewrite), (SeedIds.Scenes.Ch6Sc1, SeedIds.KbEntries.TeamAtlasOffsite),
            (SeedIds.Scenes.Ch9Sc1, SeedIds.KbEntries.ConwaysLaw),
        ], ignoreOrder: true);
        (await connection.ExecuteScalarAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.Ch4Sc1 }))!
            .ShouldContain($"::kb-addition{{id=\"{SeedIds.KbProgressions.Ch4Sc1GoodhartsLaw}\"}}");
    }
}
