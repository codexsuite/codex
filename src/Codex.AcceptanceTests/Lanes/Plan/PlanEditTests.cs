using System.Net;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Plan;

// Editing a card in place: the summary with an @ mention, a label from the card menu, a failed save, and the link to Write.
[TestFixture]
public class PlanEditTests : SeededAcceptanceTest
{
    private static string BookId => SeedIds.Books.ProductEngineering.ToString();

    private static string ScenePath(Guid sceneId) => $"/api/scenes/{sceneId}";

    [Test, Order(1)]
    public async Task Summary_WhenMentionTyped_ShouldSaveChip()
    {
        var plan = new PlanScreen(Page);
        var scene = SeedIds.Scenes.Ch1Sc1;
        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/kb", "GET", () => Page.GotoAsync(Links.Plan(BookId)));
        await plan.SceneSummaryEditor(scene).ClickAsync();
        await Page.Keyboard.PressAsync("End");
        await plan.SceneSummaryEditor(scene).PressSequentiallyAsync(" @Goodhart");
        await Expect(plan.MentionOption(SeedIds.KbEntries.GoodhartsLaw)).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Enter");

        var response = await Page.RunAndWaitForApiAsync(ScenePath(scene), "PATCH", () => plan.Toolbar.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        using var saved = JsonDocument.Parse(await response.TextAsync());
        saved.RootElement.GetProperty("summary").GetString()!.ShouldContain($"(kb:{SeedIds.KbEntries.GoodhartsLaw})");
        await Expect(plan.SaveStatus).ToHaveAttributeAsync("data-state", "saved");

        await Page.ReloadAsync();
        await Expect(plan.SceneSummary(scene)).ToContainTextAsync("Goodhart's Law");
    }

    [Test, Order(2)]
    public async Task Label_WhenPicked_ShouldShowOnCard()
    {
        var plan = new PlanScreen(Page);
        var scene = SeedIds.Scenes.Ch1Sc1;
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(plan.Label(scene, SeedIds.Labels.MaybeCut)).ToHaveCountAsync(0);
        await plan.Scene(scene).HoverAsync();
        await plan.SceneMore(scene).ClickAsync();
        await plan.SceneLabels(scene).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"{ScenePath(scene)}/labels", "PUT",
            () => plan.LabelOption(SeedIds.Labels.MaybeCut).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Label(scene, SeedIds.Labels.MaybeCut)).ToContainTextAsync("Maybe cut");
        await Expect(plan.Label(scene, SeedIds.Labels.ReadyToWrite)).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Autosave_WhenServerFails_ShouldShowUnsaved()
    {
        var plan = new PlanScreen(Page);
        var scene = SeedIds.Scenes.Ch2Sc1;
        await Page.GotoAsync(Links.Plan(BookId));
        await Page.RouteAsync($"**{ScenePath(scene)}", route => route.Request.Method == "PATCH"
            ? route.FulfillAsync(new RouteFulfillOptions
            {
                Status = (int)HttpStatusCode.InternalServerError,
                ContentType = "application/json",
                Body = """{"error":"Something went wrong.","code":"internal_error"}""",
            })
            : route.ContinueAsync());
        await plan.SceneSummaryEditor(scene).ClickAsync();
        await Page.Keyboard.PressAsync("End");
        await plan.SceneSummaryEditor(scene).PressSequentiallyAsync(" Unsaved words.");

        var response = await Page.RunAndWaitForApiAsync(ScenePath(scene), "PATCH", () => plan.Toolbar.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.InternalServerError);
        await Expect(plan.SaveStatus).ToHaveAttributeAsync("data-state", "unsaved");
        await Expect(plan.SceneSummary(scene)).ToContainTextAsync("Unsaved words.");
    }

    [Test, Order(4)]
    public async Task OpenInWrite_ShouldLinkToSceneHash()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.Scene(SeedIds.Scenes.Ch5Sc2).HoverAsync();
        await plan.SceneMore(SeedIds.Scenes.Ch5Sc2).ClickAsync();

        await Expect(plan.SceneOpenInWrite(SeedIds.Scenes.Ch5Sc2)).ToHaveAttributeAsync("href", Links.Write(BookId, "#scene=5.2"));
    }

    [Test, Order(5)]
    public async Task Label_WhenSaveFails_ShouldToastAndKeepLabels()
    {
        var plan = new PlanScreen(Page);
        var scene = SeedIds.Scenes.Ch3Sc3;
        await Page.GotoAsync(Links.Plan(BookId));
        await Page.RouteAsync($"**{ScenePath(scene)}/labels", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = (int)HttpStatusCode.BadRequest,
            ContentType = "application/json",
            Body = """{"error":"Every label must exist and belong to this book or its series.","code":"validation"}""",
        }));
        await plan.Scene(scene).HoverAsync();
        await plan.SceneMore(scene).ClickAsync();
        await plan.SceneLabels(scene).ClickAsync();

        await plan.LabelOption(SeedIds.Labels.Drafting).ClickAsync();

        await Expect(plan.Toast).ToContainTextAsync("Could not save the labels");
        await Expect(plan.Label(scene, SeedIds.Labels.Drafting)).ToHaveCountAsync(0);
        await Expect(plan.Label(scene, SeedIds.Labels.NeedsResearch)).ToBeVisibleAsync();
    }
}
