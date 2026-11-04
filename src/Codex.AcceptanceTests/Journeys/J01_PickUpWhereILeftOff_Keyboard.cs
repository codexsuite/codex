using System.Net;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J1 · Pick up where I left off (#16 Part 2), keyboard only (#16 review section D): the same path as
// J01_PickUpWhereILeftOff, driven with Tab, Enter, Space and Escape and no pointer after the start screen.
[TestFixture, Category("J1")]
public class J01_PickUpWhereILeftOff_Keyboard : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc2;
    private static readonly Guid Beat = SeedIds.Beats.Ch5Sc2OutcomeMetrics;
    private static readonly Guid Generation = SeedIds.AiGenerations.Ch5Sc2Pending;

    [Test, Order(1)]
    public async Task Step1_WhenTileReachedByTabAndEnterPressed_ShouldOpenPlan()
    {
        await Page.GotoAsync(Links.Library());

        await Page.ActivateAsync(new LibraryShelf(Page).Tile(BookId));

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/plan$"));
        await Expect(new PlanScreen(Page).Board).ToBeVisibleAsync();
    }

    [Test, Order(2)]
    public async Task Step2_WhenSceneMenuOpenedFromTheKeyboard_ShouldOpenInWriteAtCh5Sc2()
    {
        var plan = new PlanScreen(Page);
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Plan(BookId));

        await Page.ActivateAsync(plan.SceneMore(Scene));
        await Page.ActivateAsync(plan.SceneOpenInWrite(Scene));

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/write#scene=5\\.2$"));
        await Expect(write.SceneHeading(Scene)).ToBeInViewportAsync();
    }

    [Test, Order(3)]
    public async Task Step3_WhenApplyReachedByTabAndEnterPressed_ShouldWriteProseAndSave()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");

        var applied = await Page.RunAndWaitForApiAsync($"/api/generations/{Generation}/apply", "POST", () => Page.ActivateAsync(write.DraftApply(Beat)));

        applied.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(write.Draft(Beat)).ToHaveCountAsync(0);
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync("Outcome metrics rot");
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE id = @id", ("id", Generation))).ShouldBe("applied");
    }

    [Test, Order(4)]
    public async Task Step4_WhenLabelChangedFromTheKeyboard_ShouldSaveDraftingAndReturnFocusToTheAddButton()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.SceneLabels(Scene)).ToHaveTextAsync("Needs research");

        await Page.TabToAsync(write.SceneLabelAdd(Scene));
        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/labels", "PUT", async () =>
        {
            await Page.Keyboard.PressAsync("Enter");
            await Page.ActivateAsync(write.LabelOption(SeedIds.Labels.Drafting), "Space");
        });
        response.Status.ShouldBe((int)HttpStatusCode.OK);
        var removed = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/labels", "PUT", () =>
            Page.ActivateAsync(write.LabelOption(SeedIds.Labels.NeedsResearch), "Space"));
        removed.Status.ShouldBe((int)HttpStatusCode.OK);
        await Page.Keyboard.PressAsync("Escape");

        await Expect(write.LabelOption(SeedIds.Labels.Drafting)).ToHaveCountAsync(0);
        await Expect(write.SceneLabelAdd(Scene)).ToBeFocusedAsync();
        // A list, so the check waits out the render that drops Needs research (one string fails at once on two chips).
        await Expect(write.SceneLabels(Scene)).ToHaveTextAsync(["Drafting"]);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM scene_label WHERE scene_id = @scene AND label_id = @label AND archived_at IS NULL",
            ("scene", Scene), ("label", SeedIds.Labels.Drafting))).ShouldBe(1);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM scene_label WHERE scene_id = @scene AND label_id = @label AND archived_at IS NULL",
            ("scene", Scene), ("label", SeedIds.Labels.NeedsResearch))).ShouldBe(0);
    }
}
