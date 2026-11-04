using System.Net;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Plan;

// Northbound has one empty act: add its first chapter, then its first scene, and write the scene's summary.
[TestFixture]
public class PlanStructureTests : SeededAcceptanceTest
{
    private static string PlanUrl => Links.Plan(SeedIds.Books.Northbound.ToString());

    private static Guid _chapterId;

    private static Guid _sceneId;

    [Test, Order(1)]
    public async Task NewChapter_ShouldAppear()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);

        var response = await Page.RunAndWaitForApiAsync($"/api/acts/{SeedIds.Acts.NorthboundUntitled}/chapters", "POST",
            () => plan.NewChapter(SeedIds.Acts.NorthboundUntitled).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.TextAsync());
        _chapterId = created.RootElement.GetProperty("id").GetGuid();
        await Expect(plan.Chapter(_chapterId)).ToContainTextAsync("Chapter 1");
        await Expect(plan.Chapter(_chapterId)).ToContainTextAsync("New chapter");
        await Expect(plan.ActMeta(SeedIds.Acts.NorthboundUntitled)).ToContainTextAsync("1 chapter");
    }

    [Test, Order(2)]
    public async Task NewScene_ShouldAppearAndAutosaveSummary()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await Expect(plan.Chapter(_chapterId)).ToBeVisibleAsync();
        var response = await Page.RunAndWaitForApiAsync($"/api/chapters/{_chapterId}/scenes", "POST",
            () => plan.NewScene(_chapterId).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.TextAsync());
        _sceneId = created.RootElement.GetProperty("id").GetGuid();
        await Expect(plan.SceneNumber(_sceneId)).ToHaveTextAsync("Scene 1");
        await Expect(plan.SceneSummaryEditor(_sceneId)).ToBeFocusedAsync();

        await plan.SceneSummaryEditor(_sceneId).PressSequentiallyAsync("A harbour at dawn.");
        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{_sceneId}", "PATCH", () => plan.Toolbar.ClickAsync());

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Page.ReloadAsync();
        await Expect(plan.SceneSummary(_sceneId)).ToContainTextAsync("A harbour at dawn.");
    }

    [Test, Order(3)]
    public async Task NewScene_WhenChapterWasArchivedElsewhere_ShouldBeRefusedAndToast()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await Expect(plan.Chapter(_chapterId)).ToBeVisibleAsync();
        await Page.RouteAsync($"**/api/chapters/{_chapterId}/scenes", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = (int)HttpStatusCode.NotFound,
            ContentType = "application/json",
            Body = """{"error":"Chapter not found.","code":"not_found"}""",
        }));

        await plan.NewScene(_chapterId).ClickAsync();

        await Expect(plan.Toast).ToContainTextAsync("Could not add the scene: Chapter not found.");
        await Expect(plan.SceneNumbers).ToHaveCountAsync(1);
    }
}
