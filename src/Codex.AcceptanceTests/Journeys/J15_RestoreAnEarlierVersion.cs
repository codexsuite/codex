using System.Net;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J15 · Undo an AI apply / restore an earlier version (docs/journeys-wave5.md): Ch 5 · Sc 2 gets the seed's pending Opus
// draft applied and a hand-typed sentence; the scene's Version history compares, restores, undoes, and walks back to
// the imported version and forward to the draft again (its three source links go and come back with the text).
[TestFixture, Category("J15")]
public class J15_RestoreAnEarlierVersion : JourneyTest
{
    private const string Sentence = " We measured outcomes, not output.";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Scene = SeedIds.Scenes.Ch5Sc2;
    private static readonly Guid Beat = SeedIds.Beats.Ch5Sc2OutcomeMetrics;

    // N: the draft's word count, read from the panel in step 1 (never hard-coded).
    private static int _n;

    private Task<int> WordsAsync() => ScalarSqlAsync<int>("SELECT markdown_word_count(body)::int FROM prose WHERE scene_id = @scene", ("scene", Scene));

    private Task<long> RevisionsAsync() => ScalarSqlAsync<long>("SELECT count(*) FROM prose_revision WHERE scene_id = @scene", ("scene", Scene));

    private Task<long> LiveUsesAsync() =>
        ScalarSqlAsync<long>("SELECT count(*) FROM reference_use WHERE scene_id = @scene AND archived_at IS NULL", ("scene", Scene));

    private async Task<Guid> RevisionAsync(string source) =>
        await ScalarSqlAsync<Guid>("SELECT id FROM prose_revision WHERE scene_id = @scene AND source = @source ORDER BY created_at DESC, id DESC LIMIT 1", ("scene", Scene), ("source", source));

    private async Task OpenHistoryAsync(WritePageObject write)
    {
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.Scene(Scene)).ToBeVisibleAsync();
        await write.Scene(Scene).HoverAsync();
        await write.SceneMore(Scene).ClickAsync();
        await write.SceneHistory(Scene).ClickAsync();
        await Expect(write.History).ToBeVisibleAsync();
    }

    [Test, Order(1)]
    public async Task Step1_OnChapter5Scene2_ShouldShowAnEmptySceneAndThePendingOpusDraft()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));

        await Expect(write.Draft(Beat)).ToHaveAttributeAsync("data-state", "pending");
        var meta = await write.DraftMeta(Beat).InnerTextAsync();
        _n = int.Parse(Regex.Match(meta, @"([\d,]+) words").Groups[1].Value.Replace(",", ""));
        _n.ShouldBeGreaterThan(0);
        meta.ShouldContain("Opus 5.5");
        (await WordsAsync()).ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task Step2_WhenApplied_ShouldWriteNWordsAndCutAnAiApplyRevision()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.DraftApply(Beat)).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/generations/{SeedIds.AiGenerations.Ch5Sc2Pending}/apply", "POST", () => write.DraftApply(Beat).ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.Draft(Beat)).ToHaveCountAsync(0);
        (await WordsAsync()).ShouldBe(_n);
        (await RevisionsAsync()).ShouldBe(2);
        (await ScalarSqlAsync<Guid?>("SELECT generation_id FROM prose_revision WHERE scene_id = @scene AND source = 'ai_apply'", ("scene", Scene)))
            .ShouldBe(SeedIds.AiGenerations.Ch5Sc2Pending);
    }

    [Test, Order(3)]
    public async Task Step3_WhenASentenceIsTyped_ShouldSaveNPlusFiveWordsAsAnEditedRevision()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();

        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/prose", "PUT", async () =>
        {
            var box = await write.ProseEditor(Scene).BoundingBoxAsync() ?? throw new InvalidOperationException("editor not on screen");
            await write.ProseEditor(Scene).ClickAsync(new LocatorClickOptions { Position = new Position { X = 12, Y = box.Height - 8 } });
            await Page.AfterEditorClickAsync();
            await Page.Keyboard.PressAsync("Control+End");
            await Page.Keyboard.TypeAsync(Sentence);
            await write.Manuscript.FocusAsync();
        });

        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 10_000 });
        (await WordsAsync()).ShouldBe(_n + 5);
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", Scene)) ?? "").TrimEnd().ShouldEndWith(Sentence);
        (await RevisionsAsync()).ShouldBe(3);
    }

    [Test, Order(4)]
    public async Task Step4_WhenVersionHistoryIsOpened_ShouldListThreeRowsNewestFirstWithOneCurrent()
    {
        var write = new WritePageObject(Page);

        await OpenHistoryAsync(write);

        await Expect(write.HistoryRows).ToHaveCountAsync(3);
        await Expect(write.HistoryRows.Nth(0)).ToContainTextAsync("Edited");
        await Expect(write.HistoryRows.Nth(0)).ToContainTextAsync($"{_n + 5:N0} words");
        await Expect(write.HistoryRows.Nth(1)).ToContainTextAsync("AI draft applied");
        await Expect(write.HistoryRows.Nth(1)).ToContainTextAsync("Opus 5.5 · attempt 1");
        await Expect(write.HistoryRows.Nth(1)).ToContainTextAsync($"{_n:N0} words");
        await Expect(write.HistoryRows.Nth(2)).ToContainTextAsync("Imported");
        await Expect(write.HistoryRows.Nth(2)).ToContainTextAsync("0 words");
        await Expect(write.HistoryCurrent).ToHaveCountAsync(1);
        await Expect(write.HistoryRows.Nth(0).GetByTestId(WriteTestIds.HistoryCurrent)).ToBeVisibleAsync();
    }

    [Test, Order(5)]
    public async Task Step5_WhenTheDraftRowIsSelected_ShouldShowFiveAddedWordsAndMinusFive()
    {
        var write = new WritePageObject(Page);
        await OpenHistoryAsync(write);

        await write.HistoryRow(await RevisionAsync("ai_apply")).ClickAsync();

        await Expect(write.HistoryAdded).ToHaveTextAsync("5");
        await Expect(write.HistoryRemoved).ToHaveTextAsync("0");
        await Expect(write.HistoryDelta).ToHaveTextAsync("−5 words");
    }

    [Test, Order(6)]
    public async Task Step6_WhenRestoreIsConfirmed_ShouldPutTheDraftBackWithARestoreRevisionAndAToastThatUndoes()
    {
        var write = new WritePageObject(Page);
        await OpenHistoryAsync(write);
        var draft = await RevisionAsync("ai_apply");
        await write.HistoryRow(draft).ClickAsync();

        await write.HistoryRestore.ClickAsync();
        await Expect(write.HistoryConfirmText).ToContainTextAsync("The current text stays in history.");
        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{Scene}/prose/revisions/{draft}/restore", "POST", () => write.HistoryConfirm.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.Toast).ToContainTextAsync("Restored");
        await Expect(write.HistoryRows).ToHaveCountAsync(4);
        (await WordsAsync()).ShouldBe(_n);
        (await RevisionsAsync()).ShouldBe(4);
        (await ScalarSqlAsync<string>("SELECT source FROM prose_revision WHERE scene_id = @scene ORDER BY created_at DESC, id DESC LIMIT 1", ("scene", Scene))).ShouldBe("restore");
        await Expect(write.SceneWords(Scene)).ToContainTextAsync($"{_n:N0}");

        // Step 7, on the same page (the toast lives only as long as it): Undo brings the typed sentence back, as a restore.
        await write.ToastUndo.ClickAsync();

        await Expect(write.HistoryRows).ToHaveCountAsync(5);
        (await WordsAsync()).ShouldBe(_n + 5);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM prose_revision WHERE scene_id = @scene AND source = 'restore'", ("scene", Scene))).ShouldBe(2);
    }

    [Test, Order(8)]
    public async Task Step8_WhenTheImportedVersionIsRestored_ShouldEmptyTheSceneAndArchiveTheThreeUses()
    {
        var write = new WritePageObject(Page);
        (await LiveUsesAsync()).ShouldBe(3);
        await OpenHistoryAsync(write);
        await write.HistoryRow(await RevisionAsync("import")).ClickAsync();

        await write.HistoryRestore.ClickAsync();
        await write.HistoryConfirm.ClickAsync();

        await Expect(write.Toast).ToContainTextAsync("Restored");
        (await WordsAsync()).ShouldBe(0);
        (await LiveUsesAsync()).ShouldBe(0);
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE id = @id", ("id", SeedIds.AiGenerations.Ch5Sc2Pending))).ShouldBe("applied");
    }

    [Test, Order(9)]
    public async Task Step9_WhenTheDraftVersionIsRestoredAgain_ShouldBringTheThreeUsesBack()
    {
        var write = new WritePageObject(Page);
        await OpenHistoryAsync(write);
        await write.HistoryRow(await RevisionAsync("ai_apply")).ClickAsync();

        await write.HistoryRestore.ClickAsync();
        await write.HistoryConfirm.ClickAsync();

        await Expect(write.Toast).ToContainTextAsync("Restored");
        (await WordsAsync()).ShouldBe(_n);
        (await LiveUsesAsync()).ShouldBe(3);
    }

    [Test, Order(10)]
    public async Task Step10_OnPlan_ShouldShowTheSceneCardWithNWords()
    {
        var plan = new PlanScreen(Page);

        await Page.GotoAsync(Links.Plan(BookId));

        await Expect(plan.SceneWords(Scene)).ToContainTextAsync($"{_n:N0}");
    }
}
