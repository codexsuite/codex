using System.Net;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J16 · Restructure beyond reorder (docs/journeys-wave5.md): add, rename, move and archive chapters, move a scene to
// another chapter, split and merge scenes, and check the rules R1, R3 and R5 hold. Steps accumulate on one seed clone.
[TestFixture, Category("J16")]
public class J16_RestructureBeyondReorder : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static Guid _newChapter;
    private static Guid _splitScene;

    private const string LiveWordsSql =
        """
        SELECT coalesce(sum(p.word_count), 0)::bigint FROM prose p JOIN scene s ON s.id = p.scene_id
        JOIN chapter c ON c.id = s.chapter_id
        WHERE s.book_id = @book AND s.archived_at IS NULL AND c.archived_at IS NULL
        """;

    private Task<long> LiveWordsAsync() => ScalarSqlAsync<long>(LiveWordsSql, ("book", SeedIds.Books.ProductEngineering));

    private Task<long> LiveScenesAsync() => ScalarSqlAsync<long>(
        "SELECT count(*) FROM scene WHERE book_id = @book AND archived_at IS NULL", ("book", SeedIds.Books.ProductEngineering));

    private Task<int> MarkerAsync(Guid reference) =>
        ScalarSqlAsync<int>("SELECT number::int FROM reference_stats WHERE reference_id = @id", ("id", reference));

    private static async Task EnsureOpenAsync(PlanScreen plan, Guid act)
    {
        await Expect(plan.ActToggle(act)).ToBeVisibleAsync();
        if (await plan.ActToggle(act).GetAttributeAsync("aria-expanded") == "false")
        {
            await plan.ActToggle(act).ClickAsync();
        }
    }

    private async Task RenameAsync(PlanScreen plan, Guid chapter, string title)
    {
        await plan.Chapter(chapter).HoverAsync();
        await plan.ChapterMore(chapter).ClickAsync();
        await plan.ChapterRename(chapter).ClickAsync();
        await plan.ChapterTitleInput(chapter).FillAsync(title);
        var response = await Page.RunAndWaitForApiAsync($"/api/chapters/{chapter}", "PATCH", () => plan.ChapterTitleInput(chapter).PressAsync("Enter"));
        response.Status.ShouldBe((int)HttpStatusCode.OK);
    }

    [Test, Order(1)]
    public async Task Step1_NewChapterInAct3_ShouldBeChapter11WithOneScene()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await EnsureOpenAsync(plan, SeedIds.Acts.Practices);

        var response = await Page.RunAndWaitForApiAsync($"/api/acts/{SeedIds.Acts.Practices}/chapters", "POST",
            () => plan.NewChapter(SeedIds.Acts.Practices).ClickAsync());
        response.Status.ShouldBe((int)HttpStatusCode.Created);
        using (var created = JsonDocument.Parse(await response.TextAsync()))
        {
            _newChapter = created.RootElement.GetProperty("id").GetGuid();
        }

        await RenameAsync(plan, _newChapter, "Metrics That Mislead");
        await Expect(plan.Chapter(_newChapter).GetByTestId(PlanTestIds.ChapterHeading)).ToContainTextAsync("Chapter 11");
        await Expect(plan.ChapterTitle(_newChapter)).ToHaveTextAsync("Metrics That Mislead");
        var sceneResponse = await Page.RunAndWaitForApiAsync($"/api/chapters/{_newChapter}/scenes", "POST",
            () => plan.NewScene(_newChapter).ClickAsync());
        using var scene = JsonDocument.Parse(await sceneResponse.TextAsync());
        var sceneId = scene.RootElement.GetProperty("id").GetGuid();
        await plan.SceneSummaryEditor(sceneId).PressSequentiallyAsync("Where dashboards go wrong.");
        await Page.RunAndWaitForApiAsync($"/api/scenes/{sceneId}", "PATCH", () => plan.Toolbar.ClickAsync());

        (await ScalarSqlAsync<long>("SELECT count(*) FROM chapter WHERE book_id = @b AND archived_at IS NULL", ("b", SeedIds.Books.ProductEngineering))).ShouldBe(11);
        (await LiveScenesAsync()).ShouldBe(16);
        (await LiveWordsAsync()).ShouldBe(13_703);
    }

    [Test, Order(2)]
    public async Task Step2_RenameCh7_ShouldKeepTheSlugAndShowTheTitleOnTheWriteReference()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await EnsureOpenAsync(plan, SeedIds.Acts.TheProductEngineer);

        await RenameAsync(plan, SeedIds.Chapters.Ch7, "Shipping Small, Safely");

        await Expect(plan.ChapterTitle(SeedIds.Chapters.Ch7)).ToHaveTextAsync("Shipping Small, Safely");
        (await ScalarSqlAsync<string>("SELECT slug FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.Ch7))).ShouldBe("shipping-small");
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        // The reference keeps its live number; the chapter's current title is its tooltip.
        await Expect(write.ChapterRefsIn(SeedIds.Scenes.Ch2Sc1).Nth(1)).ToHaveTextAsync("§ Chapter 7");
        await Expect(write.ChapterRefsIn(SeedIds.Scenes.Ch2Sc1).Nth(1)).ToHaveAttributeAsync("title", "Shipping Small, Safely");
    }

    [Test, Order(3)]
    public async Task Step3_AltDownOnCh7Grip_ShouldMoveItIntoAct3KeepingChapter7()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await EnsureOpenAsync(plan, SeedIds.Acts.TheProductEngineer);
        await plan.ChapterGrip(SeedIds.Chapters.Ch7).FocusAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/chapters/{SeedIds.Chapters.Ch7}/position", "PUT",
            () => plan.ChapterGrip(SeedIds.Chapters.Ch7).PressAsync("Alt+ArrowDown"));

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.ActMeta(SeedIds.Acts.TheProductEngineer)).ToContainTextAsync("2 chapters");
        await Expect(plan.ActMeta(SeedIds.Acts.Practices)).ToContainTextAsync("5 chapters");
        await Expect(plan.Chapter(SeedIds.Chapters.Ch7).GetByTestId(PlanTestIds.ChapterHeading)).ToContainTextAsync("Chapter 7");
        (await ScalarSqlAsync<Guid>("SELECT act_id FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.Ch7))).ShouldBe(SeedIds.Acts.Practices);
    }

    [Test, Order(4)]
    public async Task Step4_MoveCh8Sc1ToCh3_ShouldBecomeCh3Sc4AndLeaveCh8Empty()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await EnsureOpenAsync(plan, SeedIds.Acts.Practices);
        await EnsureOpenAsync(plan, SeedIds.Acts.TheProductEngineer);
        await plan.Scene(SeedIds.Scenes.Ch8Sc1).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch8Sc1).ClickAsync();
        await plan.SceneMove(SeedIds.Scenes.Ch8Sc1).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/position", "PUT",
            () => plan.SceneMoveTarget(SeedIds.Chapters.Ch3).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Chapter(SeedIds.Chapters.Ch3).GetByTestId(PlanTestIds.Scene(SeedIds.Scenes.Ch8Sc1))).ToBeVisibleAsync();
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch8Sc1)).ToHaveTextAsync("Scene 4");
        await Expect(plan.ChapterEmpty(SeedIds.Chapters.Ch8)).ToBeVisibleAsync();
        (await ScalarSqlAsync<long>(
            "SELECT coalesce(sum(p.word_count), 0)::bigint FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.chapter_id = @c AND s.archived_at IS NULL",
            ("c", SeedIds.Chapters.Ch3))).ShouldBe(4_792);
        (await LiveWordsAsync()).ShouldBe(13_703);
    }

    [Test, Order(5)]
    public async Task Step5_AfterTheMove_MarkersFollowTheNewReadingOrder()
    {
        (await MarkerAsync(SeedIds.References.VelocityTrap)).ShouldBe(3);
        (await MarkerAsync(SeedIds.References.GoodhartMonetary)).ShouldBe(4);
        (await ScalarSqlAsync<int>("SELECT quoted_words FROM reference_stats WHERE reference_id = @id", ("id", SeedIds.References.VelocityTrap))).ShouldBe(342);
        (await ScalarSqlAsync<string>("SELECT status FROM reference_clearance WHERE reference_id = @id", ("id", SeedIds.References.VelocityTrap))).ShouldBe("risk");
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM reference_use_outline WHERE reference_id = @id AND scene_id IN (@a, @b)",
            ("id", SeedIds.References.VelocityTrap), ("a", SeedIds.Scenes.Ch8Sc1), ("b", SeedIds.Scenes.Ch4Sc1))).ShouldBe(2);
    }

    [Test, Order(6)]
    public async Task Step6_SplitCh1Sc1_ShouldMakeTwoScenesWithTheSameWords()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(SeedIds.Scenes.Ch1Sc1)).ToBeVisibleAsync();
        var before = await LiveScenesAsync();
        await write.PlaceCaretAtStartAsync(SeedIds.Scenes.Ch1Sc1);
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Home");

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch1Sc1}/split", "POST", async () =>
        {
            await Page.Keyboard.TypeAsync("/split");
            await write.SlashItem(WriteTestIds.SlashNewSceneFromHere).ClickAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        using var result = JsonDocument.Parse(await response.TextAsync());
        _splitScene = result.RootElement.GetProperty("newSceneId").GetGuid();
        await Expect(write.Scene(_splitScene)).ToBeVisibleAsync();
        (await LiveScenesAsync()).ShouldBe(before + 1);
        (await LiveWordsAsync()).ShouldBe(13_703);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene_kb WHERE scene_id = @id AND archived_at IS NULL", ("id", _splitScene)))
            .ShouldBe(await ScalarSqlAsync<long>("SELECT count(*) FROM scene_kb WHERE scene_id = @id AND archived_at IS NULL", ("id", SeedIds.Scenes.Ch1Sc1)));
        (await ScalarSqlAsync<string>("SELECT coalesce(summary, '') FROM scene WHERE id = @id", ("id", _splitScene))).ShouldBe("");
    }

    [Test, Order(7)]
    public async Task Step7_MergeCh3Sc2IntoSc1_ShouldJoinTheTextAndArchiveTheAbsorbedScene()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await EnsureOpenAsync(plan, SeedIds.Acts.TheProductEngineer);
        await plan.Scene(SeedIds.Scenes.Ch3Sc2).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch3Sc2).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch3Sc2}/merge-previous", "POST",
            () => plan.SceneMerge(SeedIds.Scenes.Ch3Sc2).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Scene(SeedIds.Scenes.Ch3Sc2)).ToHaveCountAsync(0);
        await Expect(plan.SceneWords(SeedIds.Scenes.Ch3Sc1)).ToContainTextAsync("4,630");
        await Expect(plan.ArchivedToggle).ToContainTextAsync("Archived (1)");
        (await ScalarSqlAsync<Guid>("SELECT scene_id FROM reference_use WHERE id = @id", ("id", SeedIds.ReferenceUses.AgileManifestoWebCh3Sc2))).ShouldBe(SeedIds.Scenes.Ch3Sc1);
        (await MarkerAsync(SeedIds.References.AgileManifestoWeb)).ShouldBe(2);
        (await LiveScenesAsync()).ShouldBe(16);
    }

    [Test, Order(8)]
    public async Task Step8_DeleteCh10_ShouldDropShipItFridayFromTheCountsAndUndoBringsItBack()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await EnsureOpenAsync(plan, SeedIds.Acts.Practices);
        await Expect(new BookBar(Page).ReferencesBadge).ToHaveTextAsync("3");
        await plan.Chapter(SeedIds.Chapters.Ch10).HoverAsync();
        await plan.ChapterMore(SeedIds.Chapters.Ch10).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/chapters/{SeedIds.Chapters.Ch10}/archive", "POST",
            () => plan.ChapterDelete(SeedIds.Chapters.Ch10).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Toast).ToContainTextAsync("Chapter archived · 1 scene · 1 source use");
        await Expect(new BookBar(Page).ReferencesBadge).ToHaveTextAsync("2");
        (await ScalarSqlAsync<string>("SELECT status FROM reference_clearance WHERE reference_id = @id", ("id", SeedIds.References.ShipItFriday))).ShouldBe("not_cited");
        (await LiveWordsAsync()).ShouldBe(13_293);

        var undo = await Page.RunAndWaitForApiAsync($"/api/chapters/{SeedIds.Chapters.Ch10}/restore", "POST",
            () => plan.ToastUndo.ClickAsync());

        undo.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(new BookBar(Page).ReferencesBadge).ToHaveTextAsync("3");
        (await ScalarSqlAsync<string>("SELECT status FROM reference_clearance WHERE reference_id = @id", ("id", SeedIds.References.ShipItFriday))).ShouldNotBe("not_cited");
        (await LiveWordsAsync()).ShouldBe(13_703);
    }

    [Test, Order(9)]
    public async Task Step9_HymnsArchivedChapterLink_ShouldKeepItsTitleAndOpenPlansArchivedList()
    {
        var write = new WritePageObject(Page);
        var hymns = SeedIds.Books.HymnsForMachines.ToString();
        await Page.GotoAsync(Links.Write(hymns));
        var link = write.ChapterRefsIn(SeedIds.Scenes.HymnsSc1);
        var title = await ScalarSqlAsync<string>("SELECT title FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.HymnsArchivedCh));
        var body = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @id", ("id", SeedIds.Scenes.HymnsSc1));

        await Expect(link.First).ToHaveTextAsync(title!);
        await Expect(link.First).ToHaveAttributeAsync("data-archived", "true");
        await Expect(link.First).ToHaveAttributeAsync("title", "This chapter is archived");

        await link.First.ClickAsync();

        var plan = new PlanScreen(Page);
        await Expect(plan.ArchivedList).ToBeVisibleAsync();
        await Expect(plan.ArchivedChapter(SeedIds.Chapters.HymnsArchivedCh)).ToBeVisibleAsync();
        await Expect(plan.RestoreChapter(SeedIds.Chapters.HymnsArchivedCh)).ToBeVisibleAsync();
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @id", ("id", SeedIds.Scenes.HymnsSc1))).ShouldBe(body);
    }

    [Test, Order(10)]
    public async Task Step10_RestoreAScene_ShouldAskWhenItsChapterIsArchivedThenRestoreBoth()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await EnsureOpenAsync(plan, SeedIds.Acts.Practices);
        await plan.Scene(SeedIds.Scenes.Ch10Sc1).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch10Sc1).ClickAsync();
        await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch10Sc1}/archive", "POST", () => plan.SceneArchive(SeedIds.Scenes.Ch10Sc1).ClickAsync());
        await plan.Chapter(SeedIds.Chapters.Ch10).HoverAsync();
        await plan.ChapterMore(SeedIds.Chapters.Ch10).ClickAsync();
        await Page.RunAndWaitForApiAsync($"/api/chapters/{SeedIds.Chapters.Ch10}/archive", "POST", () => plan.ChapterDelete(SeedIds.Chapters.Ch10).ClickAsync());
        await plan.ArchivedToggle.ClickAsync();
        await plan.Restore(SeedIds.Scenes.Ch10Sc1).ClickAsync();
        await Expect(plan.RestorePrompt).ToContainTextAsync("Where to From Here");

        // Declining leaves the scene archived, and focus returns to the Restore button.
        await plan.RestoreCancel.ClickAsync();
        await Expect(plan.RestorePrompt).ToHaveCountAsync(0);
        await Expect(plan.Restore(SeedIds.Scenes.Ch10Sc1)).ToBeFocusedAsync();
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM scene WHERE id = @id", ("id", SeedIds.Scenes.Ch10Sc1))).ShouldNotBeNull();

        await plan.Restore(SeedIds.Scenes.Ch10Sc1).ClickAsync();
        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch10Sc1}/restore", "POST",
            () => plan.RestoreBoth.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM chapter WHERE id = @id", ("id", SeedIds.Chapters.Ch10))).ShouldBeNull();
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM scene WHERE id = @id", ("id", SeedIds.Scenes.Ch10Sc1))).ShouldBeNull();
        (await LiveWordsAsync()).ShouldBe(13_703);
        await Expect(new BookBar(Page).ReferencesBadge).ToHaveTextAsync("3");
    }
}
