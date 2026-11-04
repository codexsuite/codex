using System.Net.Http.Json;
using Codex.Contracts;
using Codex.Contracts.KnowledgeBase;
using Codex.TestSupport.Fixtures;

namespace Codex.WebApi.Tests.KnowledgeBase;

// GET /api/kb/entries/{id} over the seeded Product Engineering book.
[TestFixture]
public class SeedKbEntryTests : ApiFixture
{
    [Test]
    public async Task Get_WhenGoodhartsLaw_ShouldHaveItsTimelineNoteSourceAndUse()
    {
        var entry = (await Client.GetFromJsonAsync<KbEntryDto>($"/api/kb/entries/{SeedIds.KbEntries.GoodhartsLaw}"))!;

        entry.Name.ShouldBe("Goodhart's Law");
        entry.CategoryId.ShouldBe(SeedIds.KbCategories.Laws);
        entry.Timeline.Select(n => n.SceneNumber).ShouldBe(["4.1"]);
        entry.UsedIn.ShouldContain(u => u.Number == "4.1" && u.Kinds.Any(k => k.Kind == "timeline"));
        entry.Source.ShouldNotBeNull().Title.ShouldContain("Monetary Management");
        entry.PositionInCategory.ShouldBeInRange(1, entry.CategoryEntryCount);
        entry.CreatedLabel.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task Get_WhenKentBeck_ShouldListMentionedSceneWithSnippet()
    {
        var entry = (await Client.GetFromJsonAsync<KbEntryDto>($"/api/kb/entries/{SeedIds.KbEntries.KentBeck}"))!;

        var use = entry.UsedIn.Single(u => u.Number == "3.1");
        use.Kinds.ShouldContain(k => k.Kind == "mention");
        use.Snippet.ShouldNotBeNullOrEmpty();
        use.Snippet.ShouldNotContain("](");
    }

    // The sidebar fetch backs every KB screen and its use counts come from the kb_entry_use view.
    [Test]
    public async Task GetBookKb_WhenSeedBook_ShouldCountUses()
    {
        var kb = (await Client.GetFromJsonAsync<KbDto>($"/api/books/{SeedIds.Books.ProductEngineering}/kb"))!;

        kb.Entries.Single(e => e.Id == SeedIds.KbEntries.KentBeck).SceneCount.ShouldBeGreaterThan(0);
    }
}
