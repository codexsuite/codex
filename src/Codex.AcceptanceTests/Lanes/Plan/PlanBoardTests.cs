using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Plan;

// Product Engineering's structure from the seed (#16 section 1.3): three acts, ten chapters, fifteen scenes.
[TestFixture]
public class PlanBoardTests : SeededAcceptanceTest
{
    private static readonly string[] SceneNumbersInOrder =
    [
        "1.1", "2.1", "3.1", "3.2", "3.3", "4.1", "4.2", "5.1", "5.2", "6.1", "6.2", "7.1", "8.1", "9.1", "10.1",
    ];

    // The board numbers a scene within its chapter ("Scene 2"); the outline keeps the API's "3.2".
    private static string PositionInChapter(string number) => number[(number.IndexOf('.') + 1)..];

    private static string PlanUrl(string? hash = null) => Links.Plan(SeedIds.Books.ProductEngineering.ToString(), hash);

    [Test, Order(1)]
    public async Task Board_ShouldMatchSeedActsChaptersAndScenes()
    {
        var plan = new PlanScreen(Page);

        await Page.GotoAsync(PlanUrl());

        await Expect(plan.Act(SeedIds.Acts.Introduction)).ToHaveAttributeAsync("data-collapsed", "false");
        await Expect(plan.Act(SeedIds.Acts.TheProductEngineer)).ToHaveAttributeAsync("data-collapsed", "false");
        await Expect(plan.Act(SeedIds.Acts.Practices)).ToHaveAttributeAsync("data-collapsed", "true");
        await Expect(plan.ActMeta(SeedIds.Acts.Introduction)).ToContainTextAsync("4 chapters · 7 scenes");
        await Expect(plan.ActMeta(SeedIds.Acts.TheProductEngineer)).ToContainTextAsync("3 chapters · 5 scenes");
        await Expect(plan.ActMeta(SeedIds.Acts.Practices)).ToContainTextAsync("3 chapters · 3 scenes");

        // Acts 1 and 2 are open: chapters 1 to 7 in columns, scenes numbered within their chapter.
        await Expect(plan.ChapterHeadings).ToHaveCountAsync(7);
        await Expect(plan.Chapter(SeedIds.Chapters.Ch8)).ToHaveCountAsync(0);
        await Expect(plan.SceneNumbers).ToHaveTextAsync(SceneNumbersInOrder.Take(12).Select(n => $"Scene {PositionInChapter(n)}").ToArray());
        await Expect(plan.Chapter(SeedIds.Chapters.Ch5).GetByTestId(PlanTestIds.ChapterHeading)).ToContainTextAsync("Chapter 5");
        await Expect(plan.Chapter(SeedIds.Chapters.Ch5).GetByTestId(PlanTestIds.ChapterHeading)).ToContainTextAsync("Owning Outcomes");

        // A card: labels, linked entries, words and the pending-draft mark come from the outline.
        await Expect(plan.Label(SeedIds.Scenes.Ch5Sc1, SeedIds.Labels.Drafting)).ToContainTextAsync("Drafting");
        await Expect(plan.Label(SeedIds.Scenes.Ch5Sc1, SeedIds.Labels.Drafting)).ToHaveAttributeAsync("data-tone", "info");
        await Expect(plan.Entry(SeedIds.Scenes.Ch5Sc1, SeedIds.KbEntries.MartyCagan)).ToHaveTextAsync("Marty Cagan");
        await Expect(plan.SceneWords(SeedIds.Scenes.Ch5Sc1)).ToHaveTextAsync(new Regex(@"^[\d,]+ words$"));
        await Expect(plan.SceneWords(SeedIds.Scenes.Ch6Sc2)).ToHaveTextAsync("Not written");
        await Expect(plan.ScenePending(SeedIds.Scenes.Ch5Sc2)).ToBeVisibleAsync();
        await Expect(plan.ScenePending(SeedIds.Scenes.Ch5Sc1)).ToHaveCountAsync(0);
        await Expect(plan.Scene(SeedIds.Scenes.Ch6Sc2)).ToContainTextAsync("Summarise this scene…");
        await Expect(new BookBar(Page).TabPlan).ToBeVisibleAsync();
    }

    [Test, Order(2)]
    public async Task Act3_WhenExpanded_ShouldShowItsChapters()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl());
        await Expect(plan.Act(SeedIds.Acts.Practices)).ToHaveAttributeAsync("data-collapsed", "true");
        await Expect(plan.Chapter(SeedIds.Chapters.Ch8)).ToHaveCountAsync(0);

        await plan.ActToggle(SeedIds.Acts.Practices).ClickAsync();

        await Expect(plan.Act(SeedIds.Acts.Practices)).ToHaveAttributeAsync("data-collapsed", "false");
        await Expect(plan.Chapter(SeedIds.Chapters.Ch8)).ToBeVisibleAsync();
        await Expect(plan.Chapter(SeedIds.Chapters.Ch9)).ToBeVisibleAsync();
        await Expect(plan.Chapter(SeedIds.Chapters.Ch10)).ToBeVisibleAsync();
        await Expect(plan.Label(SeedIds.Scenes.Ch8Sc1, SeedIds.Labels.MaybeCut)).ToHaveAttributeAsync("data-tone", "danger");

        // The choice is remembered for the book.
        await Page.ReloadAsync();
        await Expect(plan.Chapter(SeedIds.Chapters.Ch8)).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Outline_ShouldListSameOrderAsBoard()
    {
        var plan = new PlanScreen(Page);
        // The chapter link opens the third act whatever the earlier tests left stored.
        await Page.GotoAsync(PlanUrl("#chapter=10"));
        await Expect(plan.Chapter(SeedIds.Chapters.Ch10)).ToBeVisibleAsync();
        var boardOrder = await plan.SceneNumbers.AllTextContentsAsync();

        await plan.ViewOutline.ClickAsync();

        await Expect(plan.Outline).ToBeVisibleAsync();
        await Expect(plan.Board).ToHaveCountAsync(0);
        var outlineOrder = await plan.OutlineSceneNumbers.AllTextContentsAsync();
        boardOrder.Select(n => n.Replace("Scene ", string.Empty)).ShouldBe(SceneNumbersInOrder.Select(PositionInChapter));
        outlineOrder.ShouldBe(SceneNumbersInOrder);

        // The view is remembered for the book (the chapter link would switch back to the board, so drop it first).
        await Page.GotoAsync(PlanUrl());
        await Page.ReloadAsync();
        await Expect(plan.Outline).ToBeVisibleAsync();
        await plan.ViewBoard.ClickAsync();
        await Expect(plan.Board).ToBeVisibleAsync();
    }

    [Test, Order(4)]
    public async Task Cards_WhenKbHashSet_ShouldHighlightLinkedScenes()
    {
        var plan = new PlanScreen(Page);

        await Page.GotoAsync(PlanUrl(Links.Hash(("kb", SeedIds.KbEntries.GoodhartsLaw.ToString()), ("chapter", "10"))));

        await Expect(plan.Scene(SeedIds.Scenes.Ch4Sc1)).ToHaveAttributeAsync("data-emphasis", "hit");
        await Expect(plan.Scene(SeedIds.Scenes.Ch5Sc2)).ToHaveAttributeAsync("data-emphasis", "hit");
        await Expect(plan.Scene(SeedIds.Scenes.Ch3Sc1)).ToHaveAttributeAsync("data-emphasis", "dim");
        await Expect(plan.Scene(SeedIds.Scenes.Ch10Sc1)).ToHaveAttributeAsync("data-emphasis", "dim");
    }

    [Test, Order(5)]
    public async Task SceneHash_WhenInCollapsedAct_ShouldExpandItAndScrollToTheCard()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl());
        await Page.EvaluateAsync("() => window.localStorage.clear()");

        await Page.GotoAsync(PlanUrl("#scene=9.1"));
        await Page.ReloadAsync();

        await Expect(plan.Act(SeedIds.Acts.Practices)).ToHaveAttributeAsync("data-collapsed", "false");
        await Expect(plan.Scene(SeedIds.Scenes.Ch9Sc1)).ToBeInViewportAsync();
    }

    [Test, Order(6)]
    public async Task SceneHash_WhenNoSuchScene_ShouldBeIgnored()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl());
        await Page.EvaluateAsync("() => window.localStorage.clear()");

        await Page.GotoAsync(PlanUrl("#scene=99.9"));
        await Page.ReloadAsync();

        await Expect(plan.Board).ToBeVisibleAsync();
        await Expect(plan.Act(SeedIds.Acts.Practices)).ToHaveAttributeAsync("data-collapsed", "true");
        await Expect(new AppShell(Page).BookBar.ErrorBadLink).ToHaveCountAsync(0);
        await Expect(plan.LoadError).ToHaveCountAsync(0);
    }
}
