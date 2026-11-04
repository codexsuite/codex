using System.Net;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J4 · Outline and restructure (#16 Part 2): move Shipping Small ahead of Discovery, archive the Maybe-cut scene,
// check the Outline view, then check the chapter reference in Write and the Velocity Trap's new status in References.
[TestFixture, Category("J4")]
public class J04_OutlineAndRestructure : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Discovery = SeedIds.Chapters.Ch6;
    private static readonly Guid ShippingSmall = SeedIds.Chapters.Ch7;

    private static ILocator Heading(PlanScreen plan, Guid chapter) => plan.Chapter(chapter).GetByTestId(PlanTestIds.ChapterHeading);

    [Test, Order(1)]
    public async Task Step1_OnPlan_Act2ShouldListChapters5Owning6Discovery7ShippingSmall()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));

        await Expect(Heading(plan, SeedIds.Chapters.Ch5)).ToContainTextAsync("Chapter 5");
        await Expect(Heading(plan, SeedIds.Chapters.Ch5)).ToContainTextAsync("Owning Outcomes");
        await Expect(Heading(plan, Discovery)).ToContainTextAsync("Chapter 6");
        await Expect(Heading(plan, Discovery)).ToContainTextAsync("Discovery");
        await Expect(Heading(plan, ShippingSmall)).ToContainTextAsync("Chapter 7");
        await Expect(Heading(plan, ShippingSmall)).ToContainTextAsync("Shipping Small");
    }

    [Test, Order(2)]
    public async Task Step2_WhenCh7GripDraggedBeforeCh6_ShouldShowDropLineAndMoveIt()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(Heading(plan, ShippingSmall)).ToContainTextAsync("Chapter 7");
        await plan.Chapter(Discovery).ScrollIntoViewIfNeededAsync();
        await plan.ChapterGrip(ShippingSmall).ScrollIntoViewIfNeededAsync();
        var from = await plan.ChapterGrip(ShippingSmall).BoundingBoxAsync() ?? throw new InvalidOperationException("grip not on screen");
        var onto = await plan.Chapter(Discovery).BoundingBoxAsync() ?? throw new InvalidOperationException("chapter not on screen");

        var response = await Page.RunAndWaitForApiAsync($"/api/chapters/{ShippingSmall}/position", "PUT", async () =>
        {
            await Page.Mouse.MoveAsync(from.X + from.Width / 2, from.Y + from.Height / 2);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(from.X - 10, from.Y + 10, new() { Steps = 4 });
            await Page.Mouse.MoveAsync(onto.X + 20, onto.Y + 20, new() { Steps = 10 });
            await Expect(plan.DropLine).ToBeVisibleAsync();
            await Page.Mouse.UpAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.DropLine).ToHaveCountAsync(0);
    }

    [Test, Order(3)]
    public async Task Step3_AfterMove_ShippingSmallShouldBeCh6AndDiscoveryCh7()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(Heading(plan, ShippingSmall)).ToContainTextAsync("Chapter 6");
        await Expect(Heading(plan, Discovery)).ToContainTextAsync("Chapter 7");
        await Page.ReloadAsync();
        await Expect(Heading(plan, ShippingSmall)).ToContainTextAsync("Chapter 6");
        await Expect(Heading(plan, Discovery)).ToContainTextAsync("Chapter 7");
        (await ScalarSqlAsync<string>("SELECT position FROM chapter WHERE id = @id", ("id", ShippingSmall)))
            .ShouldBeLessThan(await ScalarSqlAsync<string>("SELECT position FROM chapter WHERE id = @id", ("id", Discovery)) ?? "", StringComparer.Ordinal);
    }

    [Test, Order(4)]
    public async Task Step4_WhenCh8Sc1Archived_ShouldOfferUndoAndSetArchivedAt()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.ActToggle(SeedIds.Acts.Practices).ClickAsync();
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch8Sc1)).ToHaveTextAsync("Scene 1");
        await plan.Scene(SeedIds.Scenes.Ch8Sc1).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch8Sc1).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/archive", "POST",
            () => plan.SceneArchive(SeedIds.Scenes.Ch8Sc1).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Scene(SeedIds.Scenes.Ch8Sc1)).ToHaveCountAsync(0);
        await Expect(plan.ToastUndo).ToBeVisibleAsync();
        await Expect(plan.Toast).ToContainTextAsync("Scene archived");
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM scene WHERE id = @id", ("id", SeedIds.Scenes.Ch8Sc1))).ShouldNotBeNull();
    }

    [Test, Order(5)]
    public async Task Step5_OnOutlineView_ShouldShowNewOrderThenBoardAgain()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(plan.Board).ToBeVisibleAsync();

        await plan.ViewOutline.ClickAsync();

        await Expect(plan.Outline).ToBeVisibleAsync();
        var order = await plan.OutlineSceneNumbers.AllTextContentsAsync();
        var shipping = await plan.OutlineScene(SeedIds.Scenes.Ch7Sc1).TextContentAsync();
        var discovery = await plan.OutlineScene(SeedIds.Scenes.Ch6Sc1).TextContentAsync();
        order.ShouldNotBeEmpty();
        shipping.ShouldNotBeNull().ShouldContain("6.");
        discovery.ShouldNotBeNull().ShouldContain("7.");
        await Expect(plan.OutlineScene(SeedIds.Scenes.Ch8Sc1)).ToHaveCountAsync(0);

        await plan.ViewBoard.ClickAsync();
        await Expect(plan.Board).ToBeVisibleAsync();
    }

    [Test, Order(6)]
    public async Task Step6_OnWrite_AboutThisBookShouldReferToSectionChapter6()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await new BookBar(Page).TabWrite.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/write"));
        // Ch 2 · Sc 1 (About this book) points at Owning Outcomes (still 5) and Shipping Small (7, now 6); the stored
        // links are (chapter:<id>) (docs/prose-format.md), so the prose does not change.
        await Expect(write.ChapterRefsIn(SeedIds.Scenes.Ch2Sc1)).ToHaveTextAsync(["§ Chapter 5", "§ Chapter 6"]);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM prose, chapter WHERE prose.scene_id = @scene AND chapter.id = @chapter AND prose.body LIKE '%(chapter:' || chapter.id || ')%'",
            ("scene", SeedIds.Scenes.Ch2Sc1), ("chapter", ShippingSmall))).ShouldBe(1);
    }

    [Test, Order(7)]
    public async Task Step7_Watch_VelocityTrapShouldDropTo214WordsAndMoveToNeedsCheck()
    {
        var references = new ReferencesScreen(Page);
        await Page.GotoAsync(Links.References(BookId));

        await Expect(references.Row(SeedIds.References.VelocityTrap)).ToBeVisibleAsync();
        await Expect(references.Row(SeedIds.References.VelocityTrap).GetByTestId(ReferencesTestIds.RowStatus)).ToHaveTextAsync("Needs check");
        await Expect(references.Row(SeedIds.References.VelocityTrap)).Not.ToContainTextAsync("342");
        await references.Row(SeedIds.References.VelocityTrap).ClickAsync();
        await Expect(new SourcePanel(Page).VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(references.Row(SeedIds.References.VelocityTrap)).ToContainTextAsync("214 words");
    }
}
