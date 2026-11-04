using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J16 negative paths (docs/journeys-wave5.md): each one must leave the data unchanged. They share one fresh seed because
// none of them writes.
[TestFixture, Category("J16")]
public class J16_RestructureBeyondReorder_Negative : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();

    [Test, Order(1)]
    public async Task Rename_WhenTheTitleIsEmpty_ShouldShowAnInlineErrorAndChangeNothing()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        var before = await ScalarSqlAsync<string>("SELECT title FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.Ch1));
        await plan.Chapter(SeedIds.Chapters.Ch1).HoverAsync();
        await plan.ChapterMore(SeedIds.Chapters.Ch1).ClickAsync();
        await plan.ChapterRename(SeedIds.Chapters.Ch1).ClickAsync();

        await plan.ChapterTitleInput(SeedIds.Chapters.Ch1).FillAsync("   ");
        await plan.ChapterTitleInput(SeedIds.Chapters.Ch1).PressAsync("Enter");

        await Expect(plan.ChapterTitleError(SeedIds.Chapters.Ch1)).ToHaveTextAsync("The title cannot be empty.");
        await plan.ChapterTitleInput(SeedIds.Chapters.Ch1).PressAsync("Escape");
        await Expect(plan.ChapterTitle(SeedIds.Chapters.Ch1)).ToHaveTextAsync(before!);
        (await ScalarSqlAsync<string>("SELECT title FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.Ch1))).ShouldBe(before);
    }

    [Test, Order(2)]
    public async Task Archive_WhenItIsTheOnlyActOfNorthbound_ShouldBeRefusedAndChangeNothing()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(SeedIds.Books.Northbound.ToString()));
        await plan.ActMore(SeedIds.Acts.NorthboundUntitled).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/acts/{SeedIds.Acts.NorthboundUntitled}/archive", "POST",
            () => plan.ActDelete(SeedIds.Acts.NorthboundUntitled).ClickAsync());

        response.Status.ShouldNotBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM act WHERE id = @id", ("id", SeedIds.Acts.NorthboundUntitled))).ShouldBeNull();
    }

    [Test, Order(3)]
    public async Task Drag_WhenEscapeIsPressed_ShouldRemoveTheDropLineAndMoveNothing()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(plan.Chapter(SeedIds.Chapters.Ch1)).ToBeVisibleAsync();
        var position = await ScalarSqlAsync<string>("SELECT position FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.Ch3));
        await plan.Chapter(SeedIds.Chapters.Ch3).HoverAsync();
        var from = await plan.ChapterGrip(SeedIds.Chapters.Ch3).BoundingBoxAsync() ?? throw new InvalidOperationException("grip not on screen");
        var onto = await plan.Chapter(SeedIds.Chapters.Ch1).BoundingBoxAsync() ?? throw new InvalidOperationException("chapter not on screen");

        await Page.Mouse.MoveAsync(from.X + from.Width / 2, from.Y + from.Height / 2);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(from.X + 10, from.Y + 10, new() { Steps = 4 });
        await Page.Mouse.MoveAsync(onto.X + 20, onto.Y + 20, new() { Steps = 10 });
        await Expect(plan.DropLine).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Page.Mouse.UpAsync();

        await Expect(plan.DropLine).ToHaveCountAsync(0);
        (await ScalarSqlAsync<string>("SELECT position FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.Ch3))).ShouldBe(position);
    }

    [Test, Order(4)]
    public async Task Merge_WhenTheEarlierSceneHasAPendingDraft_ShouldBeBlocked()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.Scene(SeedIds.Scenes.Ch5Sc2).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch5Sc2).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch5Sc2}/merge-previous", "POST",
            () => plan.SceneMerge(SeedIds.Scenes.Ch5Sc2).ClickAsync());

        response.Status.ShouldBe(422);
        await Expect(plan.Toast).ToContainTextAsync("Apply or discard the draft first");
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM scene WHERE id = @id", ("id", SeedIds.Scenes.Ch5Sc2))).ShouldBeNull();
    }

    [Test, Order(5)]
    public async Task SceneMenu_WhenTheSceneIsFirstInItsChapter_ShouldNotOfferMerge()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.Scene(SeedIds.Scenes.Ch3Sc1).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch3Sc1).ClickAsync();

        await Expect(plan.SceneArchive(SeedIds.Scenes.Ch3Sc1)).ToBeVisibleAsync();
        await Expect(plan.SceneMerge(SeedIds.Scenes.Ch3Sc1)).ToHaveCountAsync(0);
        await Expect(plan.SceneMoveTarget(SeedIds.Chapters.Ch3)).ToHaveCountAsync(0);
    }
}
