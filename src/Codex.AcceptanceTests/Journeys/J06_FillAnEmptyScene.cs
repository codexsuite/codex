using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Manuscript;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J6 · Fill an empty scene (#16 Part 2): give the empty Ch 6 · Sc 2 (Discovery) a summary with an @ mention and the
// label Needs research on Plan, open it in Write, drop a scene beat at the caret and generate a draft from it.
[TestFixture, Category("J6")]
public class J06_FillAnEmptyScene : JourneyTest
{
    private const string Summary = "What the second round of interviews taught us about ";
    private const string Instruction = "The team learns that customers never asked for the dashboard.";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch6Sc2;
    private static readonly FakeProseGenerator Generator = new(["Nobody had asked for the dashboard. ", "They had asked for fewer surprises."]);

    private static Guid _beatId;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    [Test, Order(1)]
    public async Task Step1_WhenSummaryTypedWithMention_ShouldSaveItWithTeamAtlasOffsiteChip()
    {
        var plan = new PlanScreen(Page);
        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/kb", "GET", () => Page.GotoAsync(Links.Plan(BookId)));
        await Expect(plan.Scene(Scene)).ToBeVisibleAsync();
        await Expect(plan.SceneSummaryEmpty(Scene)).ToHaveTextAsync("Summarise this scene… (@ to mention Codex)");

        await plan.SceneSummaryEmpty(Scene).ClickAsync();
        await Expect(plan.SceneSummaryEditor(Scene)).ToBeFocusedAsync();
        await plan.SceneSummaryEditor(Scene).PressSequentiallyAsync(Summary + "@Team");
        await Expect(plan.MentionOption(SeedIds.KbEntries.TeamAtlasOffsite)).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Enter");
        await plan.SceneSummaryEditor(Scene).PressSequentiallyAsync(".");

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}", "PATCH", () => plan.Toolbar.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(plan.SceneSummary(Scene)).ToContainTextAsync(Summary + "Team Atlas Offsite");
        var stored = await ScalarSqlAsync<string>("SELECT summary FROM scene WHERE id = @id", ("id", Scene));
        stored.ShouldNotBeNull().ShouldContain($"(kb:{SeedIds.KbEntries.TeamAtlasOffsite})");
        stored.ShouldStartWith(Summary);
    }

    [Test, Order(2)]
    public async Task Step2_WhenNeedsResearchPicked_ShouldShowLabelOnCardAndSaveSceneLabel()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(plan.Label(Scene, SeedIds.Labels.NeedsResearch)).ToHaveCountAsync(0);
        await plan.Scene(Scene).HoverAsync();
        await plan.SceneMore(Scene).ClickAsync();
        await plan.SceneLabels(Scene).ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/labels", "PUT", () => plan.LabelOption(SeedIds.Labels.NeedsResearch).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(plan.Label(Scene, SeedIds.Labels.NeedsResearch)).ToContainTextAsync("Needs research");
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM scene_label WHERE scene_id = @scene AND label_id = @label",
            ("scene", Scene), ("label", SeedIds.Labels.NeedsResearch))).ShouldBe(1);
    }

    [Test, Order(3)]
    public async Task Step3_WhenOpenInWriteClicked_ShouldShowSameSummaryInTheGutter()
    {
        var plan = new PlanScreen(Page);
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.Scene(Scene).HoverAsync();
        await plan.SceneMore(Scene).ClickAsync();

        await plan.SceneOpenInWrite(Scene).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/write#scene=6\\.2$"));
        await Expect(write.Scene(Scene)).ToBeInViewportAsync();
        await Expect(write.SceneSummary(Scene)).ToContainTextAsync(Summary + "Team Atlas Offsite");
        await Expect(write.SceneLabels(Scene)).ToContainTextAsync("Needs research");
    }

    [Test, Order(4)]
    public async Task Step4_WhenSceneBeatInsertedAndGenerated_ShouldWriteBeatLineAndPendingDraft()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "6.2"))));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        await Expect(write.BeatCards(Scene)).ToHaveCountAsync(0);

        var inserted = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/beats", "POST", async () =>
        {
            await write.PlaceCaretAtStartAsync(Scene);
            await Page.Keyboard.TypeAsync("/scene");
            await write.SlashItem(WriteTestIds.SlashSceneBeat).ClickAsync();
        });

        inserted.Status.ShouldBe((int)HttpStatusCode.OK);
        _beatId = JsonSerializer.Deserialize<InsertBeatResult>(await inserted.TextAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.BeatId;
        await Expect(write.BeatCards(Scene)).ToHaveCountAsync(1);
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene)))
            .ShouldNotBeNull().ShouldContain($"::beat{{id=\"{_beatId}\"}}");
        (await ScalarSqlAsync<Guid>("SELECT scene_id FROM beat WHERE id = @id", ("id", _beatId))).ShouldBe(Scene);

        await write.BeatBody(_beatId).FillAsync(Instruction);
        await Page.RunAndWaitForApiAsync($"/api/beats/{_beatId}", "PATCH", () => write.BeatBody(_beatId).BlurAsync());
        await Page.RunAndWaitForApiAsync($"/api/beats/{_beatId}/generate", "POST", () => write.BeatGenerate(_beatId).ClickAsync());

        await Expect(write.Draft(_beatId)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftText(_beatId)).ToContainTextAsync("Nobody had asked for the dashboard.");
        (await ScalarSqlAsync<string>("SELECT body FROM beat WHERE id = @id", ("id", _beatId))).ShouldBe(Instruction);
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE beat_id = @id", ("id", _beatId))).ShouldBe("pending");
    }
}
