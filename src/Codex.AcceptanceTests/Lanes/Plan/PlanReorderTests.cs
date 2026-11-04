using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Plan;

// Reordering Product Engineering's board: drag, Alt+arrows, and a write the server refuses.
[TestFixture]
public class PlanReorderTests : SeededAcceptanceTest
{
    private static string PlanUrl => Links.Plan(SeedIds.Books.ProductEngineering.ToString());

    [Test, Order(1)]
    public async Task Scene_WhenDragged_ShouldRenumber()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch3Sc3)).ToHaveTextAsync("Scene 3");
        var from = await plan.SceneGrip(SeedIds.Scenes.Ch3Sc3).BoundingBoxAsync() ?? throw new InvalidOperationException("grip not on screen");
        var onto = await plan.Scene(SeedIds.Scenes.Ch3Sc1).BoundingBoxAsync() ?? throw new InvalidOperationException("card not on screen");

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch3Sc3}/position", "PUT", async () =>
        {
            await Page.Mouse.MoveAsync(from.X + from.Width / 2, from.Y + from.Height / 2);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(from.X + 20, from.Y - 10, new() { Steps = 4 });
            await Page.Mouse.MoveAsync(onto.X + onto.Width / 2, onto.Y + onto.Height * 0.2f, new() { Steps = 10 });
            await Expect(plan.DropLine).ToBeVisibleAsync();
            await Page.Mouse.UpAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.DropLine).ToHaveCountAsync(0);
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch3Sc3)).ToHaveTextAsync("Scene 1");
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch3Sc1)).ToHaveTextAsync("Scene 2");
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch3Sc2)).ToHaveTextAsync("Scene 3");

        await Page.ReloadAsync();
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch3Sc3)).ToHaveTextAsync("Scene 1");
    }

    [Test, Order(2)]
    public async Task Chapter_WhenAltLeft_ShouldMoveBeforePrevious()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await Expect(plan.Chapter(SeedIds.Chapters.Ch6).GetByTestId(PlanTestIds.ChapterHeading)).ToContainTextAsync("Chapter 6");
        await plan.ChapterGrip(SeedIds.Chapters.Ch6).FocusAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/chapters/{SeedIds.Chapters.Ch6}/position", "PUT",
            () => Page.Keyboard.PressAsync("Alt+ArrowLeft"));

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Chapter(SeedIds.Chapters.Ch6).GetByTestId(PlanTestIds.ChapterHeading)).ToContainTextAsync("Chapter 5");
        await Expect(plan.Chapter(SeedIds.Chapters.Ch5).GetByTestId(PlanTestIds.ChapterHeading)).ToContainTextAsync("Chapter 6");
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch6Sc1)).ToHaveTextAsync("Scene 1");
        await Expect(plan.ChapterGrip(SeedIds.Chapters.Ch6)).ToBeFocusedAsync();
    }

    [Test, Order(3)]
    public async Task Scene_WhenAltUp_ShouldMoveUp()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch4Sc2)).ToHaveTextAsync("Scene 2");
        await plan.SceneGrip(SeedIds.Scenes.Ch4Sc2).FocusAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch4Sc2}/position", "PUT",
            () => Page.Keyboard.PressAsync("Alt+ArrowUp"));

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch4Sc2)).ToHaveTextAsync("Scene 1");
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch4Sc1)).ToHaveTextAsync("Scene 2");
        await Expect(plan.SceneGrip(SeedIds.Scenes.Ch4Sc2)).ToBeFocusedAsync();
    }

    [Test, Order(4)]
    public async Task Move_WhenServerFails_ShouldRevert()
    {
        var plan = new PlanScreen(Page);
        await Page.RouteAsync("**/api/scenes/*/position", route => route.FulfillAsync(new()
        {
            Status = 500,
            ContentType = "application/json",
            Body = """{"error":"Something went wrong on the server.","code":"internal_error"}""",
        }));
        await Page.GotoAsync(PlanUrl);
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch5Sc1)).ToHaveTextAsync("Scene 1");
        await plan.SceneGrip(SeedIds.Scenes.Ch5Sc1).FocusAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch5Sc1}/position", "PUT",
            () => Page.Keyboard.PressAsync("Alt+ArrowDown"));

        response.Status.ShouldBe((int)HttpStatusCode.InternalServerError);
        await Expect(plan.Toast).ToContainTextAsync("Could not move the scene");
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch5Sc1)).ToHaveTextAsync("Scene 1");
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch5Sc2)).ToHaveTextAsync("Scene 2");
    }
}
