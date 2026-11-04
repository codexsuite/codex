using System.Net.Http.Json;
using Codex.Application.Prose;
using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Codex.Contracts.Structure;
using Codex.TestSupport.Database;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Seed;

// Iron Litany on the large seed (db/seed-large): 3 acts, 30 chapters, 90 scenes, 124,870 words. The outline and the
// prose reads complete and carry the whole book, and the prose parses with the C# reader to the embeds the seed wrote.
[TestFixture]
public class SeedLargeApiTests : ApiFixture
{
    private const int TotalWords = 124870;

    protected override Task<ClonedDatabase> CloneDatabaseAsync() => CodexDatabase.CloneSeedLargeAsync();

    [Test, Order(1)]
    public async Task Outline_WhenLargeSeed_ShouldHold3Acts30Chapters90Scenes()
    {
        var outline = (await Client.GetFromJsonAsync<OutlineDto>($"/api/books/{SeedIds.Books.IronLitany}/outline"))!;

        outline.Acts.Count.ShouldBe(3);
        outline.Acts.Select(a => a.Chapters.Count).ShouldBe([10, 10, 10]);
        var chapters = outline.Acts.SelectMany(a => a.Chapters).ToList();
        chapters.Select(c => c.Number).ShouldBe(Enumerable.Range(1, 30).Select(n => (int?)n));
        chapters.ShouldAllBe(c => c.Scenes.Count == 3);
        var scenes = chapters.SelectMany(c => c.Scenes).ToList();
        scenes.Count.ShouldBe(90);
        scenes.Sum(s => s.Words).ShouldBe(TotalWords);
        scenes.Min(s => s.Words).ShouldBeGreaterThanOrEqualTo(500);
        scenes.Max(s => s.Words).ShouldBeLessThanOrEqualTo(3000);
        scenes[^1].Number.ShouldBe("30.3");
    }

    [Test, Order(2)]
    public async Task Manuscript_WhenLargeSeed_ShouldReturnEveryScene()
    {
        var manuscript = (await Client.GetFromJsonAsync<ManuscriptDto>($"/api/books/{SeedIds.Books.IronLitany}/manuscript"))!;

        manuscript.Scenes.Count.ShouldBe(90);
        manuscript.Scenes.ShouldAllBe(s => s.Version == 1 && s.Body.EndsWith('\n') && !s.Body.EndsWith("\n\n"));
    }

    [Test, Order(3)]
    public async Task Prose_WhenLargeSeed_ShouldParseToTheSeededEmbeds()
    {
        var manuscript = (await Client.GetFromJsonAsync<ManuscriptDto>($"/api/books/{SeedIds.Books.IronLitany}/manuscript"))!;

        var embeds = manuscript.Scenes.SelectMany(s => ProseDocument.Parse(s.Body).ListEmbeds()).ToList();

        embeds.Count(e => e.Kind == ProseEmbedKind.Beat).ShouldBe(18);
        embeds.Count(e => e.Kind == ProseEmbedKind.Mention).ShouldBe(30);
        embeds.Count(e => e.Kind == ProseEmbedKind.ChapterLink).ShouldBe(5);
        manuscript.Scenes.ShouldAllBe(s => ProseDocument.Parse(s.Body).ToString() == s.Body);
    }

    [Test, Order(4)]
    public async Task SingleSceneProse_WhenLargeSeed_ShouldRead()
    {
        var last = await Client.GetFromJsonAsync<ManuscriptDto>($"/api/books/{SeedIds.Books.IronLitany}/manuscript");
        var sceneId = last!.Scenes[^1].SceneId;

        var prose = (await Client.GetFromJsonAsync<ProseDto>($"/api/scenes/{sceneId}/prose"))!;

        prose.Body.ShouldBe(last.Scenes[^1].Body);
    }

    [Test, Order(5)]
    public async Task BookStats_WhenLargeSeed_ShouldCountTheWholeBook()
    {
        await using var connection = await OpenDatabaseAsync();

        var words = await connection.ExecuteScalarAsync<int>("SELECT word_count FROM book_stats WHERE book_id = @id", new { id = SeedIds.Books.IronLitany });

        words.ShouldBe(TotalWords);
    }
}
