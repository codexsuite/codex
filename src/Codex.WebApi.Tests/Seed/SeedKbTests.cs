using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// The knowledge-base half of the seed (#16 Part 1, 1.4 and 1.5). Read-only against one clone.
[TestFixture]
public class SeedKbTests
{
    private ClonedDatabase _database = null!;

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    private async Task<List<(string Category, string Tag, string Entry, string Aliases)>> ReadEntriesAsync(Guid scope)
    {
        await using var connection = await _database.OpenAsync();
        return (await connection.QueryAsync<(string Category, string Tag, string Entry, string Aliases)>(
            """
            SELECT c.name, c.tag, e.name, array_to_string(e.aliases, ', ')
            FROM kb_category c JOIN kb_entry e ON e.category_id = c.id
            WHERE c.book_id = @scope
            ORDER BY c.position, e.position
            """, new { scope })).ToList();
    }

    [Test]
    public async Task Categories_ShouldMatchIssue16()
    {
        var book = await ReadEntriesAsync(SeedIds.Books.ProductEngineering);
        var spells = await ReadEntriesAsync(SeedIds.Series.ForsakenGods);
        var ashen = await ReadEntriesAsync(SeedIds.Series.AshenCrown);
        await using var connection = await _database.OpenAsync();
        var ashenCategories = (await connection.QueryAsync<string>(
            "SELECT name FROM kb_category WHERE book_id = @id ORDER BY position", new { id = SeedIds.Series.AshenCrown })).ToList();
        var bookLevelElsewhere = await connection.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM kb_category c JOIN book b ON b.id = c.book_id
            WHERE b.kind = 'book' AND b.id <> @pe
            """, new { pe = SeedIds.Books.ProductEngineering });

        book.ShouldBe(
        [
            ("Blog Posts", "blog", "Signs of Bad Scrum", "Bad Scrum"),
            ("Blog Posts", "blog", "Feature Factory", "feature factories"),
            ("Blog Posts", "blog", "Why We Stopped Estimating", ""),
            ("Laws", "law", "Conway's Law", "Conway"),
            ("Laws", "law", "Goodhart's Law", "Goodhart"),
            ("Laws", "law", "Brooks's Law", ""),
            ("Stories", "story", "The Payments Rewrite", "payments rewrite"),
            ("Stories", "story", "Team Atlas Offsite", "Team Atlas"),
            ("Research", "research", "DORA Four Keys", "DORA"),
            ("Research", "research", "Agile Manifesto (2001)", ""),
            ("People", "person", "Kent Beck", ""),
            ("People", "person", "Marty Cagan", "Cagan"),
        ]);
        spells.Select(r => (r.Category, r.Entry)).ShouldBe(
            [("Spells", "Gravetouch"), ("Spells", "Hollow Prayer"), ("Spells", "Ember Ward")]);
        ashen.ShouldBeEmpty();
        ashenCategories.ShouldBe(["Characters", "Places"]);
        bookLevelElsewhere.ShouldBe(0);
        (await connection.ExecuteScalarAsync<string>("SELECT slug FROM kb_entry WHERE id = @id", new { id = SeedIds.KbEntries.GoodhartsLaw }))
            .ShouldBe("goodharts-law");
    }

    [Test]
    public async Task GoodhartAddition_AtCh4Sc1_ShouldBeLive()
    {
        await using var connection = await _database.OpenAsync();

        var addition = await connection.QuerySingleAsync<(Guid Entry, Guid Scene, bool Archived)>(
            "SELECT kb_entry_id, scene_id, archived_at IS NOT NULL FROM kb_progression WHERE id = @id", new { id = SeedIds.KbProgressions.Ch4Sc1GoodhartsLaw });
        var timeline = (await connection.QueryAsync<(Guid Id, int? Chapter, int Scene, int Order)>(
            "SELECT progression_id, chapter_number::int, scene_number, timeline_order::int FROM kb_progression_outline WHERE kb_entry_id = @id",
            new { id = SeedIds.KbEntries.GoodhartsLaw })).ToList();

        addition.ShouldBe((SeedIds.KbEntries.GoodhartsLaw, SeedIds.Scenes.Ch4Sc1, false));
        timeline.ShouldBe([(SeedIds.KbProgressions.Ch4Sc1GoodhartsLaw, 4, 1, 1)]);
    }

    [Test]
    public async Task BeatContexts_ShouldLinkFeatureFactoryAndGoodhart()
    {
        await using var connection = await _database.OpenAsync();

        var contexts = (await connection.QueryAsync<(Guid Beat, Guid Entry)>("SELECT beat_id, kb_entry_id FROM beat_kb")).ToList();

        contexts.ShouldBe(
            [(SeedIds.Beats.Ch4Sc1MeasuringOutput, SeedIds.KbEntries.FeatureFactory), (SeedIds.Beats.Ch5Sc2OutcomeMetrics, SeedIds.KbEntries.GoodhartsLaw)],
            ignoreOrder: true);
    }

    [Test]
    public async Task SceneKb_ShouldFollowMentions()
    {
        await using var connection = await _database.OpenAsync();

        var mentions = (await connection.QueryAsync<(Guid Scene, Guid Entry)>(
            "SELECT scene_id, kb_entry_id FROM kb_entry_use WHERE kind = 'mention'")).ToList();
        var linked = (await connection.QueryAsync<(Guid Scene, Guid Entry)>("SELECT scene_id, kb_entry_id FROM scene_kb")).ToList();

        mentions.ShouldBe(
        [
            (SeedIds.Scenes.Ch3Sc1, SeedIds.KbEntries.KentBeck),
            (SeedIds.Scenes.Ch5Sc1, SeedIds.KbEntries.MartyCagan),
            (SeedIds.Scenes.Ch5Sc1, SeedIds.KbEntries.ThePaymentsRewrite),
            (SeedIds.Scenes.Ch6Sc1, SeedIds.KbEntries.TeamAtlasOffsite),
            (SeedIds.Scenes.Ch9Sc1, SeedIds.KbEntries.ConwaysLaw),
        ], ignoreOrder: true);
        foreach (var mention in mentions)
        {
            linked.ShouldContain(mention);
        }
    }
}
