using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Lanes.Write;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Errors;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J18 · Recover from a failed save or AI call (docs/journeys-wave5.md): Ch 7 · Sc 1 loses its connection (the edit waits
// and goes by itself), meets a failing server (Retry), and is edited in two tabs (R6: last write wins, the other text is
// kept as a revision); Ch 3 · Sc 3's generation meets a rate limit and a Stop. The "No API key" error with its link
// (step 8) is main's per-user AI integrations (#71): "Connect an AI provider in Settings", tested there.
// Step numbers in the doc count from the seed's 940 words; here each part starts from what the one before left.
[TestFixture, Category("J18")]
public class J18_RecoverFromAFailedSave : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch7Sc1;
    private static readonly Guid DraftScene = SeedIds.Scenes.Ch3Sc3;
    private static readonly Guid Beat = SeedIds.Beats.Ch3Sc3BadScrum;
    private static readonly SwitchableProseGenerator Generator = new();

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<IProseGenerator>(Generator);

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    private Task<int> WordsAsync() => ScalarSqlAsync<int>("SELECT markdown_word_count(body)::int FROM prose WHERE scene_id = @scene", ("scene", Scene));

    private Task<int> VersionAsync() => ScalarSqlAsync<int>("SELECT version FROM prose WHERE scene_id = @scene", ("scene", Scene));

    private static async Task TypeAtEndAsync(WritePageObject write, string text)
    {
        var editor = write.ProseEditor(Scene);
        var box = await editor.BoundingBoxAsync() ?? throw new InvalidOperationException("editor not on screen");
        await editor.ClickAsync(new LocatorClickOptions { Position = new Position { X = 12, Y = box.Height - 8 } });
        await editor.Page.AfterEditorClickAsync();
        await editor.PressAsync("Control+End");
        await editor.PressSequentiallyAsync(text);
    }

    [Test, Order(1)]
    public async Task StepsA_WhenOffline_ShouldKeepTheEditShowOfflineAndSaveByItselfWhenBack()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=7.1"));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        var words = await WordsAsync();
        var version = await VersionAsync();
        await Page.Context.SetOfflineAsync(true);

        await TypeAtEndAsync(write, " Batch size is the strategy.");
        await write.Manuscript.FocusAsync();

        await Expect(write.SaveStatus).ToHaveTextAsync("Offline · 1 change waiting", new() { Timeout = 10_000 });
        await Expect(write.ProseEditor(Scene)).ToContainTextAsync("Batch size is the strategy.");
        (await Page.EvaluateAsync<string?>("id => window.localStorage.getItem('codex.unsaved.' + id)", Scene.ToString())).ShouldNotBeNull();
        await Page.Context.SetOfflineAsync(false);

        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 15_000 });
        (await WordsAsync()).ShouldBe(words + 5);
        (await VersionAsync()).ShouldBe(version + 1);
        (await Page.EvaluateAsync<string?>("id => window.localStorage.getItem('codex.unsaved.' + id)", Scene.ToString())).ShouldBeNull();
    }

    [Test, Order(2)]
    public async Task StepB_WhenTheServerFails_ShouldOfferRetryThatSavesTheKeptEdit()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=7.1"));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        var words = await WordsAsync();
        await Page.RouteAsync("**/api/scenes/*/prose", route =>
            string.Equals(route.Request.Method, "PUT", StringComparison.OrdinalIgnoreCase)
                ? route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 500,
                    ContentType = "application/json",
                    Body = """{"error":"Something went wrong on the server.","code":"internal_error"}""",
                })
                : route.FallbackAsync());

        await TypeAtEndAsync(write, " Ship small.");
        await write.Manuscript.FocusAsync();

        await Expect(write.SaveStatus).ToHaveTextAsync("Unsaved · Retry");
        await Expect(write.SaveStatusRetry).ToBeVisibleAsync();
        (await WordsAsync()).ShouldBe(words);
        await Page.UnrouteAsync("**/api/scenes/*/prose");

        await write.SaveStatusRetry.ClickAsync();

        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(write.SaveStatus).ToHaveTextAsync("Saved");
        (await WordsAsync()).ShouldBe(words + 2);
    }

    [Test, Order(3)]
    public async Task StepsC_WhenTwoTabsEdit_ShouldKeepTheLastWriteAndTheOtherTextAsARevision()
    {
        var tab1 = new WritePageObject(Page);
        var page2 = await Page.Context.NewPageAsync();
        var tab2 = new WritePageObject(page2);
        await Page.GotoAsync(Links.Write(BookId, "#scene=7.1"));
        await page2.GotoAsync(Links.Write(BookId, "#scene=7.1"));
        await Expect(tab1.ProseEditor(Scene)).ToBeVisibleAsync();
        await Expect(tab2.ProseEditor(Scene)).ToBeVisibleAsync();
        var baseWords = await WordsAsync();

        // Step 5: tab 2 types three words; tab 1 (no edits) says Updated elsewhere with Reload.
        await TypeAtEndAsync(tab2, " One two three.");
        await tab2.Manuscript.FocusAsync();
        await Expect(tab2.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 15_000 });
        var tab2Words = await WordsAsync();
        tab2Words.ShouldBe(baseWords + 3);
        await Expect(tab1.SaveStatus).ToHaveAttributeAsync("data-state", "remoteChanged", new() { Timeout = 15_000 });
        await Expect(tab1.SaveStatusReload).ToBeVisibleAsync();

        // Step 6: tab 1 types four words without reloading. Last write wins: its save is accepted and it says Saved.
        await TypeAtEndAsync(tab1, " Four more words here.");
        await tab1.Manuscript.FocusAsync();
        await Expect(tab1.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 15_000 });
        (await WordsAsync()).ShouldBe(baseWords + 4);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM prose_revision WHERE scene_id = @scene AND source = 'replaced' AND markdown_word_count(body) = @words",
            ("scene", Scene), ("words", tab2Words))).ShouldBe(1);
        await Expect(tab2.SaveStatus).ToHaveAttributeAsync("data-state", "remoteChanged", new() { Timeout = 15_000 });

        // Step 7: tab 2 Reload shows tab 1's text.
        await tab2.SaveStatusReload.ClickAsync();
        await Expect(tab2.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(tab2.ProseEditor(Scene)).ToContainTextAsync("Four more words here.");
        await Expect(tab2.ProseEditor(Scene)).Not.ToContainTextAsync("One two three.");
        await page2.CloseAsync();
    }

    [Test, Order(4)]
    public async Task StepD9_WhenTheProviderRateLimits_ShouldShowTheCountdownAndKeepRetryDisabled()
    {
        var write = new WritePageObject(Page);
        Generator.Current = new FakeProseGenerator([], failWith: new ApiException(ApiErrorCodes.Internal, "Anthropic is rate limiting requests. Try the draft again in a moment."));
        await Page.GotoAsync(Links.Write(BookId, "#scene=3.3"));
        await Expect(write.BeatGenerate(Beat)).ToBeEnabledAsync();

        await write.BeatGenerate(Beat).ClickAsync();

        await Expect(write.DraftError(Beat)).ToContainTextAsync("Rate limited. Try again in");
        await Expect(write.DraftRetry(Beat)).ToBeDisabledAsync();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM ai_generation WHERE beat_id = @beat AND status = 'failed'", ("beat", Beat))).ShouldBe(1);
        (await ScalarSqlAsync<int>("SELECT markdown_word_count(body)::int FROM prose WHERE scene_id = @scene", ("scene", DraftScene))).ShouldBe(0);
    }

    [Test, Order(5)]
    public async Task StepD10_WhenStoppedMidStream_ShouldKeepThePartialTextAsAPendingDraftThatApplies()
    {
        var write = new WritePageObject(Page);
        Generator.Current = new FakeProseGenerator(["Half a draft here. ", "The rest never arrives."], new TaskCompletionSource().Task);
        await Page.GotoAsync(Links.Write(BookId, "#scene=3.3"));
        await Expect(write.BeatGenerate(Beat)).ToBeEnabledAsync();
        await write.BeatGenerate(Beat).ClickAsync();
        await Expect(write.DraftCaret(Beat)).ToBeVisibleAsync();
        await Expect(write.DraftText(Beat)).ToContainTextAsync("Half a draft here.");

        await write.DraftStop(Beat).ClickAsync();

        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        await Expect(write.DraftMeta(Beat)).ToContainTextAsync("Stopped · 4 words");
        await Expect(write.DraftApply(Beat)).ToBeVisibleAsync();
        await Expect(write.DraftRetry(Beat)).ToBeVisibleAsync();
        await Expect(write.DraftDiscard(Beat)).ToBeVisibleAsync();
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE beat_id = @beat ORDER BY created_at DESC LIMIT 1", ("beat", Beat))).ShouldBe("pending");
        (await ScalarSqlAsync<string>("SELECT output FROM ai_generation WHERE beat_id = @beat ORDER BY created_at DESC LIMIT 1", ("beat", Beat))).ShouldBe("Half a draft here. ");

        var applied = await Page.RunAndWaitForApiAsync($"/api/generations/{await ScalarSqlAsync<Guid>("SELECT id FROM ai_generation WHERE beat_id = @beat AND status = 'pending'", ("beat", Beat))}/apply", "POST", () => write.DraftApply(Beat).ClickAsync());

        applied.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<int>("SELECT markdown_word_count(body)::int FROM prose WHERE scene_id = @scene", ("scene", DraftScene))).ShouldBe(4);
        await Expect(write.ProseEditor(DraftScene)).ToContainTextAsync("Half a draft here.");
    }
}
