using Codex.Contracts;
using Codex.TestSupport.Database;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// The Library half of the seed (#16 Part 1, 1.2): three series and fifteen books. Word counts come from the seeded
// prose, so the recorded totals below differ from #16's quoted figures (D3); targets and statuses are #16's.
[TestFixture]
public class SeedLibraryTests
{
    private ClonedDatabase _database = null!;

    public sealed record BookRow(string Title, int WordCount, int? Target, string Status, int Chapters, int Scenes);

    public sealed record SeriesRow(string Title, int WordCount, int BookCount, int PublishedCount);

    // Title, words, target, status, live chapters, live scenes. Product Engineering holds the manuscript; every other
    // book is deterministic filler (40 + 10 * book + 15 * scene words per scene); Northbound is empty.
    private static readonly BookRow[] Expected =
    [
        new("Players Guide", 615, 60000, "revising", 3, 6),
        new("Gamemaster's Codex", 675, 90000, "writing", 3, 6),
        new("Bestiary of the Fallen", 735, 50000, "planning", 3, 6),
        new("Ember Oath", 795, 110000, "published", 3, 6),
        new("Iron Litany", 855, 120000, "published", 3, 6),
        new("Hollow Throne", 915, 120000, "revising", 3, 6),
        new("Last Cinder", 975, 120000, "planning", 3, 6),
        new("Saltmarsh", 1035, 85000, "published", 3, 6),
        new("Undertow", 1095, 85000, "writing", 3, 6),
        new("Product Engineering", 13703, 80000, "writing", 10, 15),
        new("Product Engineering (v1)", 1155, 80000, "draft", 3, 6),
        new("The Quiet Algorithm", 1215, 70000, "writing", 3, 6),
        new("Saltwater Ledger", 1275, 90000, "published", 3, 6),
        new("Northbound", 0, 60000, "planning", 0, 0),
        new("Hymns for Machines", 980, 40000, "draft", 3, 5),
    ];

    private static readonly SeriesRow[] ExpectedSeries =
    [
        new("Forsaken Gods", 2025, 3, 0),
        new("The Ashen Crown", 3540, 4, 2),
        new("Tidewater Chronicles", 2130, 2, 1),
    ];

    [OneTimeSetUp]
    public async Task CloneSeed() => _database = await CodexDatabase.CloneSeedAsync();

    [OneTimeTearDown]
    public async Task DropClone() => await _database.DisposeAsync();

    [Test]
    public async Task BookStats_WhenSeeded_ShouldMatchRecordedTotals()
    {
        await using var connection = await _database.OpenAsync();

        var books = (await connection.QueryAsync<BookRow>(
            """
            SELECT b.title AS Title, s.word_count AS WordCount, s.target_word_count AS Target, b.status AS Status,
                   (SELECT count(*) FROM chapter_outline c WHERE c.book_id = b.id)::int AS Chapters,
                   (SELECT count(*) FROM scene_stats x WHERE x.book_id = b.id)::int     AS Scenes
            FROM book_stats s JOIN book b ON b.id = s.book_id
            WHERE b.kind = 'book'
            """)).ToList();

        books.Count.ShouldBe(15);
        foreach (var expected in Expected)
        {
            books.Single(b => b.Title == expected.Title).ShouldBe(expected);
        }
    }

    [Test]
    public async Task BookStats_ForSeries_ShouldRollUpTheirBooks()
    {
        await using var connection = await _database.OpenAsync();

        var series = (await connection.QueryAsync<SeriesRow>(
            """
            SELECT b.title AS Title, s.word_count AS WordCount, s.book_count AS BookCount, s.published_count AS PublishedCount
            FROM book_stats s JOIN book b ON b.id = s.book_id
            WHERE b.kind = 'series'
            """)).ToList();

        series.Count.ShouldBe(3);
        foreach (var expected in ExpectedSeries)
        {
            series.Single(s => s.Title == expected.Title).ShouldBe(expected);
        }
    }

    [Test]
    public async Task Library_WhenSeeded_ShouldBeOwnedByJdWithSeriesNumbers()
    {
        await using var connection = await _database.OpenAsync();

        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM book WHERE owner_id <> @jd", new { jd = SeedIds.Users.Jd })).ShouldBe(0);
        // JD, and Ana to share with (#80), who owns nothing.
        (await connection.QueryAsync<Guid>("SELECT id FROM app_user")).ShouldBe([SeedIds.Users.Jd, SeedIds.Users.Ana], ignoreOrder: true);
        var numbers = await connection.QueryAsync<string>(
            """
            SELECT s.title || ' ' || b.series_number || ' ' || b.title
            FROM book b JOIN book s ON s.id = b.series_id ORDER BY s.title, b.series_number
            """);
        numbers.ShouldBe(
        [
            "Forsaken Gods 1 Players Guide", "Forsaken Gods 2 Gamemaster's Codex", "Forsaken Gods 3 Bestiary of the Fallen",
            "The Ashen Crown 1 Ember Oath", "The Ashen Crown 2 Iron Litany", "The Ashen Crown 3 Hollow Throne", "The Ashen Crown 4 Last Cinder",
            "Tidewater Chronicles 1 Saltmarsh", "Tidewater Chronicles 2 Undertow",
        ]);
    }

    [Test]
    public async Task LastEdited_WhenSeeded_ShouldBeSpacedRelativeToProductEngineering()
    {
        await using var connection = await _database.OpenAsync();

        // Stamped when the shared template is built, so only the gaps between books are stable.
        var ago = (await connection.QueryAsync<(string Title, double Days)>(
            """
            SELECT title, extract(epoch FROM (SELECT last_edited_at FROM book WHERE id = @productEngineering) - last_edited_at) / 86400.0
            FROM book WHERE kind = 'book' AND title IN ('Product Engineering', 'Players Guide', 'Gamemaster''s Codex', 'Ember Oath')
            """, new { productEngineering = SeedIds.Books.ProductEngineering })).ToDictionary(r => r.Title, r => r.Days);

        ago["Product Engineering"].ShouldBe(0);
        ago["Players Guide"].ShouldBeInRange(2 / 24.0 - 0.1, 2 / 24.0 + 0.1);
        ago["Gamemaster's Codex"].ShouldBeInRange(0.9, 1.1);
        ago["Ember Oath"].ShouldBeInRange(595, 597);
    }
}
