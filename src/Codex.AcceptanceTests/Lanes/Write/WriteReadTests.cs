using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Write;

// The read view of the Product Engineering manuscript, from the seed. Step 5 changes the chapter order, so it is last.
[TestFixture]
public class WriteReadTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();

    [Test, Order(1)]
    public async Task Manuscript_WhenOpened_ShouldShowActsChaptersAndGutters()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId));

        await Expect(write.ActMarker(SeedIds.Acts.TheProductEngineer)).ToHaveTextAsync("Act 2 · The Product Engineer");
        await Expect(write.ChapterTitle(SeedIds.Chapters.Ch5)).ToHaveTextAsync("Owning Outcomes");
        await Expect(write.ChapterCounts(SeedIds.Chapters.Ch5)).ToHaveTextAsync("2 scenes · 3,120 words");
        await Expect(write.ChapterCopyRef(SeedIds.Chapters.Ch5)).ToHaveTextAsync("ch:owning-outcomes");
        await Expect(write.SceneHeading(SeedIds.Scenes.Ch5Sc1)).ToHaveTextAsync("Chapter 5 · Scene 1");
        await Expect(write.SceneWords(SeedIds.Scenes.Ch5Sc1)).ToHaveTextAsync("3,120 words");
        await Expect(write.SceneSummary(SeedIds.Scenes.Ch5Sc1)).ToContainTextAsync("Engineers in discovery");
        await Expect(write.SceneLabels(SeedIds.Scenes.Ch5Sc1)).ToHaveTextAsync("Drafting");
        await Expect(write.SceneEntries(SeedIds.Scenes.Ch5Sc1)).ToHaveTextAsync(["Marty Cagan", "The Payments Rewrite"]);
        await Expect(write.SceneWords(SeedIds.Scenes.Ch5Sc2)).ToHaveTextAsync("Not written");
        // 15 scenes in 10 chapters: a divider between the scenes of a chapter only (Ch3 has 3, Ch4 2, Ch5 2, Ch6 2).
        await Expect(write.Dividers).ToHaveCountAsync(5);
    }

    [Test, Order(2)]
    public async Task Hash_WhenScene5Point2_ShouldScrollAndFlash()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "5.2"))));

        await Expect(write.Scene(SeedIds.Scenes.Ch5Sc2)).ToHaveAttributeAsync("data-flash", "true");
        await Expect(write.Scene(SeedIds.Scenes.Ch5Sc2)).ToBeInViewportAsync();
        (await write.ScrollTopAsync()).ShouldBeGreaterThan(0);
        await Expect(write.Scene(SeedIds.Scenes.Ch1Sc1)).Not.ToBeInViewportAsync();
    }

    [Test, Order(3)]
    public async Task Hash_WhenChapter5_ShouldScrollToTheChapter()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("chapter", "5"))));

        await Expect(write.ChapterTitle(SeedIds.Chapters.Ch5)).ToBeInViewportAsync();
        await Expect(write.Scene(SeedIds.Scenes.Ch1Sc1)).Not.ToBeInViewportAsync();
    }

    [Test, Order(4)]
    public async Task Hash_WhenSceneUnknown_ShouldIgnoreTheTarget()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "99.9"))));

        await Expect(write.MinimapTicks).ToHaveCountAsync(15);
        await Expect(write.Scene(SeedIds.Scenes.Ch1Sc1)).ToBeInViewportAsync();
        await Expect(write.Scene(SeedIds.Scenes.Ch1Sc1)).Not.ToHaveAttributeAsync("data-flash", "true");
        (await write.ScrollTopAsync()).ShouldBe(0);
    }

    [Test, Order(5)]
    public async Task Minimap_ShouldHaveOneTickPerSeededScene()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(BookId));

        await Expect(write.MinimapTicks).ToHaveCountAsync(15);
        await Expect(write.MinimapChapters).ToHaveCountAsync(10);
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch5Sc2)).ToHaveAttributeAsync("data-kind", "pending");
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch4Sc2)).ToHaveAttributeAsync("data-kind", "empty");
        await Expect(write.MinimapTick(SeedIds.Scenes.Ch1Sc1)).ToHaveAttributeAsync("data-kind", "written");
    }

    [Test, Order(6)]
    public async Task Hymns_ShouldShowTheArchivedChapterByItsTitle()
    {
        var write = new WritePageObject(Page);

        await Page.GotoAsync(Links.Write(SeedIds.Books.HymnsForMachines.ToString()));

        // Rule R1: an archived chapter keeps working, in an archived style (J16 step 9 follows the link to Plan).
        await Expect(write.ChapterRefsIn(SeedIds.Scenes.HymnsSc1)).ToHaveTextAsync("The Hymn That Was Cut");
        await Expect(write.ChapterRefsIn(SeedIds.Scenes.HymnsSc1)).ToHaveAttributeAsync("data-archived", "true");
        await Expect(write.ChapterRefsIn(SeedIds.Scenes.HymnsSc1)).ToHaveAttributeAsync("data-missing", "false");
    }

    [Test, Order(7)]
    public async Task ChapterRef_WhenChapterReorderedInSql_ShouldShowNewNumber()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        // Ch 2 Sc 1 points at Owning Outcomes (chapter 5) and Shipping Small (chapter 7).
        await Expect(write.ChapterRefsIn(SeedIds.Scenes.Ch2Sc1)).ToHaveTextAsync(["§ Chapter 5", "§ Chapter 7"]);

        // Shipping Small moves to the front of its act: it becomes chapter 5 and Owning Outcomes chapter 6.
        await ExecuteSqlAsync(
            "UPDATE chapter SET position = 'A' WHERE id = @id",
            ("id", Guid.Parse("0199c0de-0000-7000-8000-000500000007")));
        await Page.ReloadAsync();

        await Expect(write.ChapterRefsIn(SeedIds.Scenes.Ch2Sc1)).ToHaveTextAsync(["§ Chapter 6", "§ Chapter 5"]);
        await Expect(write.ChapterCopyRef(SeedIds.Chapters.Ch5)).ToHaveTextAsync("ch:owning-outcomes");
    }
}
