using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J15 negative paths, each from its own fresh seed clone: nothing is written on Cancel, the current row cannot be
// restored, a scene edited elsewhere says so before it is restored over, and the keyboard drives the panel.
// (An archived scene's read-only history is an API test: Write does not draw archived scenes.)
[TestFixture, Category("J15")]
public class J15_RestoreAnEarlierVersion_Negative : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    // One scene per test, so the tests do not depend on each other's history (they share one seed clone).
    private static readonly Guid CancelScene = SeedIds.Scenes.Ch7Sc1;
    private static readonly Guid CurrentScene = SeedIds.Scenes.Ch8Sc1;
    private static readonly Guid ElsewhereScene = SeedIds.Scenes.Ch10Sc1;
    private static readonly Guid KeyboardScene = SeedIds.Scenes.Ch1Sc1;

    private async Task OpenHistoryAsync(WritePageObject write, Guid scene, string number)
    {
        await Page.GotoAsync(Links.Write(BookId, $"#scene={number}"));
        await Expect(write.Scene(scene)).ToBeVisibleAsync();
        await write.Scene(scene).HoverAsync();
        await write.SceneMore(scene).ClickAsync();
        await write.SceneHistory(scene).ClickAsync();
        await Expect(write.History).ToBeVisibleAsync();
    }

    // The seed gives each scene only its imported version; a saved hand edit (with its revision) gives the history a
    // second row, so there is an older version to pick.
    private async Task GiveTheSceneAnEditAsync(Guid scene)
    {
        await ScalarSqlAsync<int>(
            "UPDATE prose SET body = body || E'\\n\\nA later paragraph.', version = version + 1 WHERE scene_id = @scene RETURNING 1", ("scene", scene));
        await ScalarSqlAsync<int>(
            "INSERT INTO prose_revision (scene_id, body, source) SELECT scene_id, body, 'autosave' FROM prose WHERE scene_id = @scene RETURNING 1", ("scene", scene));
    }

    private Task<long> RevisionsAsync(Guid scene) => ScalarSqlAsync<long>("SELECT count(*) FROM prose_revision WHERE scene_id = @scene", ("scene", scene));

    private Task<Guid> ImportedAsync(Guid scene) =>
        ScalarSqlAsync<Guid>("SELECT id FROM prose_revision WHERE scene_id = @scene AND source = 'import'", ("scene", scene));

    [Test, Order(1)]
    public async Task Cancel_WhenRestoreIsNotConfirmed_ShouldWriteNoRevisionAndKeepTheText()
    {
        var write = new WritePageObject(Page);
        await GiveTheSceneAnEditAsync(CancelScene);
        var before = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", CancelScene));
        var revisions = await RevisionsAsync(CancelScene);
        await OpenHistoryAsync(write, CancelScene, "7.1");

        await write.HistoryRow(await ImportedAsync(CancelScene)).ClickAsync();
        await write.HistoryRestore.ClickAsync();
        await write.HistoryCancel.ClickAsync();

        await Expect(write.HistoryConfirm).ToHaveCountAsync(0);
        (await RevisionsAsync(CancelScene)).ShouldBe(revisions);
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", CancelScene))).ShouldBe(before);
    }

    [Test, Order(2)]
    public async Task CurrentRow_WhenSelected_ShouldHaveItsRestoreDisabledWithAReason()
    {
        var write = new WritePageObject(Page);

        await OpenHistoryAsync(write, CurrentScene, "8.1");

        await Expect(write.HistoryRows.First.GetByTestId(WriteTestIds.HistoryCurrent)).ToBeVisibleAsync();
        await Expect(write.HistoryRestore).ToBeDisabledAsync();
        await Expect(write.History).ToContainTextAsync("This is the current version");
    }

    [Test, Order(3)]
    public async Task Panel_WhenTheSceneChangedElsewhere_ShouldSayUpdatedElsewhereAndStillRestoreLastWriteWins()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId, "#scene=10.1"));
        await Expect(write.Scene(ElsewhereScene)).ToBeVisibleAsync();
        await GiveTheSceneAnEditAsync(ElsewhereScene);
        await write.Scene(ElsewhereScene).HoverAsync();
        await write.SceneMore(ElsewhereScene).ClickAsync();
        await write.SceneHistory(ElsewhereScene).ClickAsync();

        await Expect(write.HistoryUpdated).ToBeVisibleAsync();
        var imported = await ImportedAsync(ElsewhereScene);
        await write.HistoryRow(imported).ClickAsync();
        await write.HistoryRestore.ClickAsync();
        var response = await Page.RunAndWaitForApiAsync($"/api/scenes/{ElsewhereScene}/prose/revisions/{imported}/restore", "POST", () => write.HistoryConfirm.ClickAsync());

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", ElsewhereScene))).ShouldNotBeNull().ShouldNotContain("A later paragraph.");
        // The other tab's text is kept in history, as the version the restore replaced.
        (await ScalarSqlAsync<long>("SELECT count(*) FROM prose_revision WHERE scene_id = @scene AND body LIKE '%A later paragraph.%'", ("scene", ElsewhereScene))).ShouldBeGreaterThan(0);
    }

    [Test, Order(4)]
    public async Task Keyboard_WhenUsed_ShouldOpenTheMenuMoveRowsAndReturnFocusOnEscape()
    {
        var write = new WritePageObject(Page);
        await GiveTheSceneAnEditAsync(KeyboardScene);
        await Page.GotoAsync(Links.Write(BookId, "#scene=1.1"));
        await Expect(write.Scene(KeyboardScene)).ToBeVisibleAsync();

        await write.SceneMore(KeyboardScene).FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await write.SceneHistory(KeyboardScene).FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(write.History).ToBeVisibleAsync();
        await Expect(write.HistoryRows.First).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(write.HistoryRows.Nth(1)).ToBeFocusedAsync();
        await Expect(write.HistoryRows.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Page.Keyboard.PressAsync("Escape");

        await Expect(write.History).ToHaveCountAsync(0);
        await Expect(write.SceneMore(KeyboardScene)).ToBeFocusedAsync();
    }
}
