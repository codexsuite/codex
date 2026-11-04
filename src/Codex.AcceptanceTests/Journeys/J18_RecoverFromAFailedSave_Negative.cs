using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J18 negative paths: an apply while offline changes nothing, leaving with a stuck edit asks first (and Stay keeps the
// editor), and a beat with no text cannot be generated from.
[TestFixture, Category("J18")]
public class J18_RecoverFromAFailedSave_Negative : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();

    // The draft panel offers Generate (here: disabled for an empty beat) only once the user has an AI integration (#36).
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    [Test, Order(1)]
    public async Task Apply_WhenOffline_ShouldSayNotAppliedKeepTheDraftPendingAndTheProseUnchanged()
    {
        var write = new WritePageObject(Page);
        var scene = SeedIds.Scenes.Ch5Sc2;
        var beat = SeedIds.Beats.Ch5Sc2OutcomeMetrics;
        await Page.GotoAsync(Links.Write(BookId, "#scene=5.2"));
        await Expect(write.DraftApply(beat)).ToBeVisibleAsync();
        var before = await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", scene));
        await Page.Context.SetOfflineAsync(true);

        await write.DraftApply(beat).ClickAsync();

        await Expect(write.Toast).ToContainTextAsync("Not applied. You are offline");
        await Expect(write.Draft(beat)).ToHaveAttributeAsync("data-state", "pending");
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @scene", ("scene", scene))).ShouldBe(before);
        (await ScalarSqlAsync<string>("SELECT status FROM ai_generation WHERE id = @id", ("id", SeedIds.AiGenerations.Ch5Sc2Pending))).ShouldBe("pending");
        await Page.Context.SetOfflineAsync(false);
    }

    [Test, Order(2)]
    public async Task Leave_WhenAnEditIsStuck_ShouldAskAndStayWhenDeclined()
    {
        var write = new WritePageObject(Page);
        var scene = SeedIds.Scenes.Ch7Sc1;
        var asked = new List<string>();
        Page.Dialog += async (_, dialog) =>
        {
            asked.Add(dialog.Message);
            await dialog.DismissAsync();
        };
        await Page.GotoAsync(Links.Write(BookId, "#scene=7.1"));
        await Expect(write.ProseEditor(scene)).ToBeVisibleAsync();
        await Page.Context.SetOfflineAsync(true);
        var editor = write.ProseEditor(scene);
        var box = await editor.BoundingBoxAsync() ?? throw new InvalidOperationException("editor not on screen");
        await editor.ClickAsync(new LocatorClickOptions { Position = new Position { X = 12, Y = box.Height - 8 } });
        await Page.AfterEditorClickAsync();
        await editor.PressAsync("Control+End");
        await editor.PressSequentiallyAsync(" Kept offline.");
        await write.Manuscript.FocusAsync();
        await Expect(write.SaveStatus).ToContainTextAsync("Offline", new() { Timeout = 10_000 });

        await new Pages.Shell.BookBar(Page).TabPlan.ClickAsync();

        asked.ShouldHaveSingleItem().ShouldContain("You have changes that are not saved");
        await Expect(write.Root).ToBeVisibleAsync();
        await Expect(write.ProseEditor(scene)).ToContainTextAsync("Kept offline.");
        await Page.Context.SetOfflineAsync(false);
    }

    [Test, Order(3)]
    public async Task Generate_WhenTheBeatIsEmpty_ShouldBeDisabledAndWriteNoGeneration()
    {
        var write = new WritePageObject(Page);
        var beat = SeedIds.Beats.Ch3Sc3BadScrum;
        await ScalarSqlAsync<int>("UPDATE beat SET body = '' WHERE id = @beat RETURNING 1", ("beat", beat));
        await Page.GotoAsync(Links.Write(BookId, "#scene=3.3"));

        await Expect(write.BeatGenerate(beat)).ToBeDisabledAsync();

        (await ScalarSqlAsync<long>("SELECT count(*) FROM ai_generation WHERE beat_id = @beat", ("beat", beat))).ShouldBe(0);
    }
}
