using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Plan;

// Archiving the scene that holds a source use (chapter 8, scene 1 cites The Velocity Trap), then restoring it.
[TestFixture]
public class PlanArchiveTests : SeededAcceptanceTest
{
    private static string PlanUrl => Links.Plan(SeedIds.Books.ProductEngineering.ToString());

    [Test, Order(1)]
    public async Task Archive_ShouldOfferUndoAndNameUses()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await plan.ActToggle(SeedIds.Acts.Practices).ClickAsync();
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch8Sc1)).ToHaveTextAsync("Scene 1");
        await plan.Scene(SeedIds.Scenes.Ch8Sc1).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch8Sc1).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/archive", "POST",
            () => plan.SceneArchive(SeedIds.Scenes.Ch8Sc1).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Scene(SeedIds.Scenes.Ch8Sc1)).ToHaveCountAsync(0);
        await Expect(plan.Toast).ToContainTextAsync("Scene archived · 1 source use archived (The Velocity Trap)");
        await Expect(plan.ToastUndo).ToBeVisibleAsync();
        await Expect(plan.ArchivedToggle).ToHaveTextAsync("Archived (1)");
    }

    [Test, Order(2)]
    public async Task Archive_WhenAlreadyArchived_ShouldBeRefused()
    {
        var response = await Page.APIRequest.PostAsync($"{BaseUrl}/api/scenes/{SeedIds.Scenes.Ch8Sc1}/archive");

        response.Status.ShouldBe((int)HttpStatusCode.Conflict);
    }

    [Test, Order(3)]
    public async Task Restore_FromArchivedList_ShouldReturnScene()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await Expect(plan.Chapter(SeedIds.Chapters.Ch8)).ToBeVisibleAsync();
        await Expect(plan.Scene(SeedIds.Scenes.Ch8Sc1)).ToHaveCountAsync(0);
        await plan.ArchivedToggle.ClickAsync();
        await Expect(plan.ArchivedScene(SeedIds.Scenes.Ch8Sc1)).ToContainTextAsync("1 source use archived with it");

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{SeedIds.Scenes.Ch8Sc1}/restore", "POST",
            () => plan.Restore(SeedIds.Scenes.Ch8Sc1).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.SceneNumber(SeedIds.Scenes.Ch8Sc1)).ToHaveTextAsync("Scene 1");
        await Expect(plan.ArchivedToggle).ToHaveTextAsync("Archived (0)");
        await Expect(plan.Toast).ToContainTextAsync("Scene restored");
    }
}
