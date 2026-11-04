using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// The structure half of the seed (#16 Part 1, 1.3 and 1.4): Product Engineering's outline, Northbound, Hymns for
// Machines and the pending draft. Read-only against one clone.
[TestFixture]
public class SeedStructureTests
{
    private ClonedDatabase _database = null!;

    public sealed record OutlineRow(int Act, int Chapter, string Slug, int Scene, Guid SceneId, int Words, int Beats, string Labels);

    private static OutlineRow Row(int act, int chapter, string slug, int scene, Guid id, int words, int beats, string labels) =>
        new(act, chapter, slug, scene, id, words, beats, labels);

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    [Test]
    public async Task ChapterOutline_ForProductEngineering_ShouldMatchSeed()
    {
        await using var connection = await _database.OpenAsync();

        var rows = await connection.QueryAsync<OutlineRow>(
            """
            SELECT c.act_number::int AS Act, c.chapter_number::int AS Chapter, ch.slug AS Slug, ss.scene_number::int AS Scene,
                   ss.scene_id AS SceneId, ss.word_count AS Words, ss.beat_count AS Beats,
                   coalesce((SELECT string_agg(l.name, ', ' ORDER BY l.position)
                             FROM scene_label sl JOIN label l ON l.id = sl.label_id
                             WHERE sl.scene_id = ss.scene_id), '') AS Labels
            FROM chapter_outline c
            JOIN chapter ch ON ch.id = c.id
            JOIN scene_stats ss ON ss.chapter_id = c.id
            WHERE c.book_id = @book
            ORDER BY c.reading_order, ss.scene_number
            """, new { book = SeedIds.Books.ProductEngineering });

        rows.ShouldBe(
        [
            Row(1, 1, "preface", 1, SeedIds.Scenes.Ch1Sc1, 1840, 0, "Ready to write"),
            Row(1, 2, "about-this-book", 1, SeedIds.Scenes.Ch2Sc1, 1210, 0, "Ready to write"),
            Row(1, 3, "a-brief-history-of-software-practice", 1, SeedIds.Scenes.Ch3Sc1, 2650, 1, ""),
            Row(1, 3, "a-brief-history-of-software-practice", 2, SeedIds.Scenes.Ch3Sc2, 1980, 0, ""),
            Row(1, 3, "a-brief-history-of-software-practice", 3, SeedIds.Scenes.Ch3Sc3, 0, 1, "Needs research"),
            Row(1, 4, "what-went-wrong", 1, SeedIds.Scenes.Ch4Sc1, 214 + 17, 1, "Needs research"),
            Row(1, 4, "what-went-wrong", 2, SeedIds.Scenes.Ch4Sc2, 0, 0, ""),
            Row(2, 5, "owning-outcomes", 1, SeedIds.Scenes.Ch5Sc1, 3120, 2, "Drafting"),
            Row(2, 5, "owning-outcomes", 2, SeedIds.Scenes.Ch5Sc2, 0, 1, "Needs research"),
            Row(2, 6, "discovery-is-everyones-job", 1, SeedIds.Scenes.Ch6Sc1, 640, 0, "Drafting"),
            Row(2, 6, "discovery-is-everyones-job", 2, SeedIds.Scenes.Ch6Sc2, 0, 0, ""),
            Row(2, 7, "shipping-small", 1, SeedIds.Scenes.Ch7Sc1, 940, 0, "Drafting"),
            Row(3, 8, "estimation-without-theatre", 1, SeedIds.Scenes.Ch8Sc1, 128 + 34, 1, "Maybe cut"),
            Row(3, 9, "teams-and-topology", 1, SeedIds.Scenes.Ch9Sc1, 520, 0, ""),
            Row(3, 10, "where-to-from-here", 1, SeedIds.Scenes.Ch10Sc1, 410, 0, ""),
        ]);
    }

    [Test]
    public async Task ChapterOutline_ForProductEngineering_ShouldUseNamedIdsAndActTitles()
    {
        await using var connection = await _database.OpenAsync();

        var chapters = await connection.QueryAsync<Guid>(
            "SELECT id FROM chapter_outline WHERE book_id = @book ORDER BY reading_order", new { book = SeedIds.Books.ProductEngineering });
        chapters.ShouldBe(
        [
            SeedIds.Chapters.Ch1, SeedIds.Chapters.Ch2, SeedIds.Chapters.Ch3, SeedIds.Chapters.Ch4, SeedIds.Chapters.Ch5,
            SeedIds.Chapters.Ch6, SeedIds.Chapters.Ch7, SeedIds.Chapters.Ch8, SeedIds.Chapters.Ch9, SeedIds.Chapters.Ch10,
        ]);
        var acts = await connection.QueryAsync<(Guid Id, string Title)>(
            "SELECT id, title FROM act WHERE book_id = @book ORDER BY position", new { book = SeedIds.Books.ProductEngineering });
        acts.ShouldBe(
        [
            (SeedIds.Acts.Introduction, "Introduction"), (SeedIds.Acts.TheProductEngineer, "The Product Engineer"), (SeedIds.Acts.Practices, "Practices"),
        ]);
    }

    [Test]
    public async Task Beats_WhenSeeded_ShouldBeInTheirProseWithTheRightState()
    {
        await using var connection = await _database.OpenAsync();

        var beats = await connection.QueryAsync<(Guid Id, Guid SceneId, int Target, bool Hidden, bool Live, bool InOutline)>(
            """
            SELECT b.id, b.scene_id, b.target_word_count, b.is_hidden, b.archived_at IS NULL,
                   EXISTS (SELECT 1 FROM beat_outline o WHERE o.beat_id = b.id)
            FROM beat b ORDER BY b.id
            """);

        beats.ShouldBe(
        [
            (SeedIds.Beats.Ch3Sc1EarlyAgileYears, SeedIds.Scenes.Ch3Sc1, 200, true, true, true),
            (SeedIds.Beats.Ch3Sc3BadScrum, SeedIds.Scenes.Ch3Sc3, 600, false, true, true),
            (SeedIds.Beats.Ch4Sc1MeasuringOutput, SeedIds.Scenes.Ch4Sc1, 400, false, true, true),
            (SeedIds.Beats.Ch5Sc1EngineersInDiscovery, SeedIds.Scenes.Ch5Sc1, 400, false, true, true),
            (SeedIds.Beats.Ch5Sc1WhatChanged, SeedIds.Scenes.Ch5Sc1, 200, false, true, true),
            (SeedIds.Beats.Ch5Sc2OutcomeMetrics, SeedIds.Scenes.Ch5Sc2, 400, false, true, true),
            (SeedIds.Beats.Ch8Sc1WhatWeDidInstead, SeedIds.Scenes.Ch8Sc1, 600, false, true, true),
        ]);
    }

    [Test]
    public async Task Prose_WhenSeeded_ShouldBeCanonicalWithOneImportRevision()
    {
        await using var connection = await _database.OpenAsync();

        var bodies = (await connection.QueryAsync<string>("SELECT body FROM prose")).ToList();
        bodies.Count.ShouldBe(
            await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scene WHERE book_id <> @n", new { n = SeedIds.Books.Northbound }));
        foreach (var body in bodies)
        {
            (body == string.Empty || (body.EndsWith('\n') && !body.EndsWith("\n\n"))).ShouldBeTrue();
            body.ShouldNotContain("\n\n\n");
        }
        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM prose p WHERE (SELECT count(*) FROM prose_revision r WHERE r.scene_id = p.scene_id AND r.source = 'import' AND r.body = p.body) <> 1"))
            .ShouldBe(0);
    }

    [Test]
    public async Task Ch2Sc1_WhenSeeded_ShouldLinkToTheOwningOutcomesAndShippingSmallChapters()
    {
        await using var connection = await _database.OpenAsync();

        var body = await connection.QuerySingleAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.Ch2Sc1 });

        body.ShouldContain($"(chapter:{SeedIds.Chapters.Ch5})");
        body.ShouldContain($"(chapter:{SeedIds.Chapters.Ch7})");
    }

    [Test]
    public async Task Northbound_ShouldBeEmptyWithOneUntitledAct()
    {
        await using var connection = await _database.OpenAsync();

        var acts = await connection.QueryAsync<(Guid Id, string Title)>(
            "SELECT id, title FROM act WHERE book_id = @id AND archived_at IS NULL", new { id = SeedIds.Books.Northbound });
        acts.ShouldBe([(SeedIds.Acts.NorthboundUntitled, "")]);
        foreach (var table in new[] { "chapter", "scene" })
        {
            (await connection.ExecuteScalarAsync<int>($"SELECT count(*)::int FROM {table} WHERE book_id = @id", new { id = SeedIds.Books.Northbound }))
                .ShouldBe(0, table);
        }
        (await connection.ExecuteScalarAsync<int>("SELECT word_count FROM book_stats WHERE book_id = @id", new { id = SeedIds.Books.Northbound }))
            .ShouldBe(0);
    }

    [Test]
    public async Task Hymns_ShouldLinkToArchivedChapter()
    {
        await using var connection = await _database.OpenAsync();

        var body = await connection.QuerySingleAsync<string>("SELECT body FROM prose WHERE scene_id = @id", new { id = SeedIds.Scenes.HymnsSc1 });
        var archived = await connection.QuerySingleAsync<(Guid BookId, bool Archived)>(
            "SELECT book_id, archived_at IS NOT NULL FROM chapter WHERE id = @id", new { id = SeedIds.Chapters.HymnsArchivedCh });

        body.ShouldContain($"](chapter:{SeedIds.Chapters.HymnsArchivedCh})");
        archived.ShouldBe((SeedIds.Books.HymnsForMachines, true));
        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM chapter_outline WHERE id = @id", new { id = SeedIds.Chapters.HymnsArchivedCh })).ShouldBe(0);
    }

    [Test]
    public async Task PendingDraft_AtCh5Sc2_ShouldExist()
    {
        await using var connection = await _database.OpenAsync();

        var draft = await connection.QuerySingleAsync<(Guid SceneId, Guid BeatId, string Kind, string Model, string Status, string Template, int Attempt, string Output)>(
            """
            SELECT scene_id, beat_id, kind, model, status, request ->> 'template', (request ->> 'attempt')::int, output
            FROM ai_generation WHERE id = @id
            """, new { id = SeedIds.AiGenerations.Ch5Sc2Pending });

        draft.SceneId.ShouldBe(SeedIds.Scenes.Ch5Sc2);
        draft.BeatId.ShouldBe(SeedIds.Beats.Ch5Sc2OutcomeMetrics);
        (draft.Kind, draft.Model, draft.Status).ShouldBe(("beat_prose", "claude-opus-5-5", "pending"));
        (draft.Template, draft.Attempt).ShouldBe(("General purpose (Nonfiction)", 1));
        draft.Output.Split("\n\n").Length.ShouldBe(3);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM ai_generation")).ShouldBe(1);
    }
}
