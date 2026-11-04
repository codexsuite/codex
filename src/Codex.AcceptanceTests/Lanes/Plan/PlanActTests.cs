using System.Net;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Plan;

// Northbound has one empty act: add a second with New Act, name it, narrow the scope to it, then delete it and undo.
[TestFixture]
public class PlanActTests : SeededAcceptanceTest
{
    private static string PlanUrl => Links.Plan(SeedIds.Books.Northbound.ToString());

    private static Guid _actId;

    [Test, Order(1)]
    public async Task NewAct_ShouldAddAnActWithAChapterAndAScene()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{SeedIds.Books.Northbound}/acts", "POST",
            () => plan.NewAct.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.Created);
        using var created = JsonDocument.Parse(await response.TextAsync());
        _actId = created.RootElement.GetProperty("id").GetGuid();
        await Expect(plan.Act(_actId)).ToContainTextAsync("Act 2");
        await Expect(plan.ActMeta(_actId)).ToContainTextAsync("1 chapter · 1 scene");
        await Expect(plan.ActTitle(_actId)).ToBeFocusedAsync();
    }

    [Test, Order(2)]
    public async Task ActTitle_WhenTyped_ShouldSaveAndSurviveAReload()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await plan.ActRename(_actId).ClickAsync();
        await Expect(plan.ActTitle(_actId)).ToBeFocusedAsync();

        await plan.ActTitle(_actId).FillAsync("The Crossing");
        var saved = await Page.RunAndWaitForApiAsync($"/api/acts/{_actId}", "PATCH", () => plan.ActTitle(_actId).PressAsync("Enter"));

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Page.ReloadAsync();
        await Expect(plan.ActTitle(_actId)).ToHaveValueAsync("The Crossing");
    }

    [Test, Order(3)]
    public async Task Scope_WhenTheActIsPicked_ShouldShowOnlyIt()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);

        await plan.Scope.SelectOptionAsync($"act:{_actId}");

        await Expect(plan.Act(SeedIds.Acts.NorthboundUntitled)).ToHaveCountAsync(0);
        await Expect(plan.Act(_actId)).ToBeVisibleAsync();
        await Expect(plan.Stats).ToHaveTextAsync("0 words · 0 pages · 0m read");
    }

    [Test, Order(4)]
    public async Task DeleteAct_ShouldTakeItOffTheBoardAndUndoShouldBringItBack()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await plan.ActMore(_actId).ClickAsync();

        var archived = await Page.RunAndWaitForApiAsync($"/api/acts/{_actId}/archive", "POST", () => plan.ActDelete(_actId).ClickAsync());

        archived.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Act(_actId)).ToHaveCountAsync(0);
        await Expect(plan.Toast).ToContainTextAsync("Act deleted");
        var restored = await Page.RunAndWaitForApiAsync($"/api/acts/{_actId}/restore", "POST", () => plan.ToastUndo.ClickAsync());
        restored.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.ActTitle(_actId)).ToHaveValueAsync("The Crossing");
    }

    [Test, Order(5)]
    public async Task DeleteAct_WhenItIsTheLastAct_ShouldBeRefusedAndToast()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(PlanUrl);
        await plan.ActMore(_actId).ClickAsync();
        await Page.RunAndWaitForApiAsync($"/api/acts/{_actId}/archive", "POST", () => plan.ActDelete(_actId).ClickAsync());
        await Expect(plan.Act(_actId)).ToHaveCountAsync(0);

        await plan.ActMore(SeedIds.Acts.NorthboundUntitled).ClickAsync();
        var refused = await Page.RunAndWaitForApiAsync($"/api/acts/{SeedIds.Acts.NorthboundUntitled}/archive", "POST",
            () => plan.ActDelete(SeedIds.Acts.NorthboundUntitled).ClickAsync());

        refused.Status.ShouldBe((int)HttpStatusCode.Conflict);
        await Expect(plan.Toast.Last).ToContainTextAsync("A book needs at least one act.");
        await Expect(plan.Act(SeedIds.Acts.NorthboundUntitled)).ToBeVisibleAsync();
    }
}
