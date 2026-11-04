using System.Net;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J1 · Pick up where I left off (#16 Part 2): Library -> Plan -> Open in Write -> apply the pending draft -> relabel.
[TestFixture, Category("J1")]
public class J01_PickUpWhereILeftOff : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc2;
    private static readonly Guid Beat = SeedIds.Beats.Ch5Sc2OutcomeMetrics;
    private static readonly Guid Generation = SeedIds.AiGenerations.Ch5Sc2Pending;

    [Test, Order(1)]
    public async Task Step1_WhenLibraryOpens_ShouldListProductEngineeringFirstUnderStandalone()
    {
        var shelf = new LibraryShelf(Page);
        await Page.GotoAsync(Links.Library());

        await Expect(shelf.Tile(BookId)).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("library-standalone-group").GetByTestId(new Regex("^shell-tile-")).First)
            .ToHaveAttributeAsync("data-testid", $"shell-tile-{BookId}");
        await Expect(shelf.Tile(BookId)).ToContainTextAsync("Product Engineering");
        // The mock's "Today, 2:42 PM" is the Library's relative label ("just now", "12m ago" ...): the label the
        // server computes for the stored timestamp.
        var edited = await ScalarSqlAsync<DateTime>(
            "SELECT last_edited_at FROM book_stats WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineering));
        await Expect(shelf.Tile(BookId)).ToContainTextAsync(ExpectedLabels.Relative(new DateTimeOffset(edited)));
    }

    [Test, Order(2)]
    public async Task Step2_WhenTileClicked_ShouldOpenPlan()
    {
        await Page.GotoAsync(Links.Library());

        await new LibraryShelf(Page).Tile(BookId).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/plan$"));
        await Expect(new PlanScreen(Page).Board).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Step3_OnPlan_Ch5Sc2ShouldBeNeedsResearchWithNoWords()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));

        await Expect(plan.Label(Scene, SeedIds.Labels.NeedsResearch)).ToContainTextAsync("Needs research");
        // The seed gives the scene no title; the beat's line is the summary on the card.
        await Expect(plan.SceneSummary(Scene)).ToContainTextAsync("Outcome metrics that don't rot");
        await Expect(plan.SceneWords(Scene)).ToHaveTextAsync("Not written");
        (await ScalarSqlAsync<int>("SELECT word_count FROM scene_stats WHERE scene_id = @id", ("id", Scene))).ShouldBe(0);
    }

    [Test, Order(4)]
    public async Task Step4_WhenOpenInWriteClicked_ShouldScrollWriteToCh5Sc2()
    {
        var plan = new PlanScreen(Page);
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.Scene(Scene).HoverAsync();
        await plan.SceneMore(Scene).ClickAsync();

        await plan.SceneOpenInWrite(Scene).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/write#scene=5\\.2$"));
        await Expect(write.Scene(Scene)).ToBeInViewportAsync();
        await Expect(write.SceneHeading(Scene)).ToBeInViewportAsync();
    }

    [Test, Order(5)]
    public async Task Step5_OnWrite_PendingDraftShouldShowAccentTickAndPanel()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));

        await Expect(write.MinimapTick(Scene)).ToHaveAttributeAsync("data-kind", "pending");
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-generation-id", Generation.ToString());
        // Deviation: the panel reads "Opus · <n> words" and shows "· attempt n" only from attempt 2 (WriteDraftTests
        // pins that); attempt 1 is asserted through the stored row, and "3 paragraphs" through the stored output.
        await Expect(write.DraftMeta(Beat)).ToContainTextAsync("Opus");
        (await ScalarSqlAsync<int>("SELECT COALESCE((request->>'attempt')::int, 1) FROM ai_generation WHERE id = @id", ("id", Generation)))
            .ShouldBe(1);
        (await ScalarSqlAsync<int>(
            "SELECT count(*)::int FROM regexp_split_to_table(btrim((SELECT output FROM ai_generation WHERE id = @id), E'\n'), E'\n{2,}')",
            ("id", Generation))).ShouldBe(3);
    }

    [Test, Order(6)]
    public async Task Step6_WhenApplyClicked_ShouldWriteProseCutRevisionAndSave()
    {
        var write = new WritePageObject(Page);
        var release = new TaskCompletionSource();
        await Page.RouteAsync($"**/api/generations/{Generation}/apply", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");

        var applied = Page.RunAndWaitForApiAsync($"/api/generations/{Generation}/apply", "POST", () => write.DraftApply(Beat).ClickAsync());
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saving");
        release.SetResult();
        var response = await applied;

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(write.Draft(Beat)).ToHaveCountAsync(0);
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync("Outcome metrics rot");
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE id = @id", ("id", Generation))).ShouldBe("applied");
        (await ScalarSqlAsync<Guid>(
            "SELECT generation_id FROM prose_revision WHERE scene_id = @scene AND source = 'ai_apply'", ("scene", Scene)))
            .ShouldBe(Generation);
        (await ScalarSqlAsync<int>("SELECT length(body) FROM prose WHERE scene_id = @scene", ("scene", Scene))).ShouldBeGreaterThan(0);
    }

    [Test, Order(7)]
    public async Task Step7_WhenLabelChangedToDrafting_ShouldSaveAndPlanShowNewWordCount()
    {
        var write = new WritePageObject(Page);
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.SceneLabels(Scene)).ToHaveTextAsync("Needs research");

        // Gutter: add Drafting, then take Needs research off ("change the label from ... to ...").
        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/labels", "PUT", async () =>
        {
            await write.SceneLabelAdd(Scene).ClickAsync();
            await write.LabelOption(SeedIds.Labels.Drafting).ClickAsync();
        });
        response.Status.ShouldBe((int)HttpStatusCode.OK);
        var removed = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/labels", "PUT", async () =>
            await write.LabelOption(SeedIds.Labels.NeedsResearch).ClickAsync());
        removed.Status.ShouldBe((int)HttpStatusCode.OK);

        // A list, so the check waits out the render that drops Needs research (one string fails at once on two chips).
        await Expect(write.SceneLabels(Scene)).ToHaveTextAsync(["Drafting"]);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM scene_label WHERE scene_id = @scene AND label_id = @label AND archived_at IS NULL",
            ("scene", Scene), ("label", SeedIds.Labels.Drafting))).ShouldBe(1);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM scene_label WHERE scene_id = @scene AND label_id = @label AND archived_at IS NULL",
            ("scene", Scene), ("label", SeedIds.Labels.NeedsResearch))).ShouldBe(0);

        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(plan.Label(Scene, SeedIds.Labels.Drafting)).ToBeVisibleAsync();
        await Expect(plan.SceneWords(Scene)).Not.ToHaveTextAsync("Not written");
        await Expect(plan.SceneWords(Scene)).ToContainTextAsync(new Regex(@"^\d[\d,]* words?$"));
    }
}
