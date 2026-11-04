using Codex.AcceptanceTests.Harness;
using Shouldly;
using static Codex.AcceptanceTests.Lanes.MobileEditor.MobileEditorMessages;

namespace Codex.AcceptanceTests.Lanes.MobileEditor;

// What the app can tell the page after `load`: `setScene`, `sceneMeta`, `scrollTo`, a second `load`, and a read-only chapter.
[TestFixture]
public class MobileEditorHostUpdateTests : EmptyAcceptanceTest
{
    private const string BeatId = "0199c0de-0000-7000-8000-000700000009";

    [Test]
    [Order(1)]
    public async Task SetScene_WhenTheTextDiffers_ShouldReplaceTheProseWithoutPostingAChange()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Old text.\n")));
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Old text.");

        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", "New text from another device.\n", version: 2)));

        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("New text from another device.");
        await Expect(editor.SceneEditor("s1")).Not.ToContainTextAsync("Old text.");
        await editor.FlushAsync();
        (await editor.PostedOfTypeAsync("change")).ShouldBeEmpty();
    }

    [Test]
    [Order(2)]
    public async Task SetScene_WhenTheMarkdownIsSpelledDifferentlyButMeansTheSame_ShouldLeaveTheEditorAndItsUndoAlone()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "An *x* here.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync("Q");
        await editor.PostedAsync("state", "m.canUndo === true");

        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", "An _x_ here.Q\n", words: 3, version: 2)));
        await Expect(editor.SceneWords("s1")).ToHaveTextAsync("3 words");
        await editor.SendFromAppAsync(Command("undo"));

        // Only the typed character goes: the editor was not rebuilt, so its history is intact.
        await Expect(editor.SceneEditor("s1")).Not.ToContainTextAsync("Q");
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("An x here.");
    }

    [Test]
    [Order(3)]
    public async Task SetScene_WhenTheEditorHasFocusAndTheTextDiffers_ShouldKeepTheCursorWhereItWas()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha beta.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+Home");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("ArrowRight");
        await editor.WaitForCaretAsync("s1", "Alpha beta.", 2);

        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", "Alpha beta gamma.\n", version: 2)));
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("gamma");
        await Page.Keyboard.TypeAsync("X");

        var change = await editor.PostedAsync("change");
        change.GetProperty("markdown").GetString()!.ShouldContain("AlXpha beta gamma.");
    }

    [Test]
    [Order(4)]
    public async Task SetScene_WhenTypedTextWasWaitingToBeSent_ShouldDropItBecauseTheAppsTextIsTheTruth()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(" Typed.");

        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", "Replaced.\n", version: 2)));
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Replaced.");
        await editor.SendFromAppAsync(Command("blur"));

        await editor.PostedAsync("blur");
        (await editor.PostedOfTypeAsync("change")).ShouldBeEmpty();
    }

    [Test]
    [Order(5)]
    public async Task SetScene_WhenItCarriesANewBeat_ShouldDrawTheBeatLine()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n")));
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Alpha.");
        var beat = Beat(BeatId, "", "Walk the gallery.", 200);

        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", $"Alpha.\n\n::beat{{id=\"{BeatId}\"}}\n", beats: [beat], version: 2)));

        await Expect(editor.BeatInstruction(BeatId)).ToHaveTextAsync("Walk the gallery.");
        await Expect(editor.BeatLength(BeatId)).ToHaveTextAsync("200 words");
    }

    [Test]
    [Order(6)]
    public async Task SceneMeta_WhenReceived_ShouldUpdateTheCardAndLeaveTheProseAlone()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n", summary: "Old summary.", words: 1)));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(" Typed.");

        await editor.SendFromAppAsync(SceneMeta("s1", "Renamed", ["Drafting"], 1234, "New summary.", [Chip("maren", "Maren", "person")]));

        await Expect(editor.SceneTitle("s1")).ToHaveTextAsync("Renamed");
        await Expect(editor.SceneWords("s1")).ToHaveTextAsync("1,234 words");
        await Expect(editor.SceneSummary("s1")).ToHaveTextAsync("New summary.");
        await Expect(editor.SceneLabel("s1", "Drafting")).ToBeVisibleAsync();
        await Expect(editor.SceneChip("s1", "maren")).ToBeVisibleAsync();
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Alpha. Typed.");
        var change = await editor.PostedAsync("change");
        change.GetProperty("markdown").GetString()!.ShouldContain("Alpha. Typed.");
    }

    [Test]
    [Order(7)]
    public async Task ScrollTo_WhenASceneIsFarDown_ShouldBringItIntoViewAndOpenItsCard()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", LongProse()), Scene("s2", "Two", "The end.\n", summary: "Second summary.")));
        await Expect(editor.SceneCard("s2")).Not.ToBeInViewportAsync();

        await editor.SendFromAppAsync(ScrollTo("s2"));

        await Expect(editor.SceneCard("s2")).ToBeInViewportAsync();
        await Expect(editor.SceneSummary("s2")).ToBeVisibleAsync();
    }

    [Test]
    [Order(8)]
    public async Task Load_WhenItNamesAScrollTarget_ShouldDrawThatSceneInView()
    {
        var scenes = new[] { Scene("s1", "One", LongProse()), Scene("s2", "Two", "The end.\n") };

        var editor = await OpenWithAsync(LoadWith(scenes, scrollToSceneId: "s2"));

        await Expect(editor.SceneCard("s2")).ToBeInViewportAsync();
    }

    [Test]
    [Order(9)]
    public async Task Load_WhenASecondOneArrives_ShouldReplaceTheScenes()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First chapter.\n")));
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("First chapter.");

        await editor.SendFromAppAsync(Load(Scene("s9", "Nine", "Next chapter.\n")));

        await Expect(editor.SceneEditor("s9")).ToContainTextAsync("Next chapter.");
        await Expect(editor.SceneEditor("s1")).ToHaveCountAsync(0);
    }

    [Test]
    [Order(10)]
    public async Task ReadOnly_WhenLoadSaysSo_ShouldNotAcceptTypingOrCommandsButStillOpenScenes()
    {
        var editor = await OpenWithAsync(LoadWith([Scene("s1", "One", "Locked text.\n")], readOnly: true));
        await Expect(editor.SceneEditor("s1")).ToHaveAttributeAsync("contenteditable", "false");

        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await Page.Keyboard.TypeAsync("nope");
        await editor.SendFromAppAsync(Command("bold"));
        await editor.SendFromAppAsync(Command("beat"));
        await editor.SceneCard("s1").ClickAsync();

        await editor.PostedAsync("openScene");
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Locked text.");
        await Expect(editor.SceneEditor("s1")).Not.ToContainTextAsync("nope");
        (await editor.SceneEditor("s1").EvaluateAsync<int>("e => e.querySelectorAll('strong').length")).ShouldBe(0);
        await editor.FlushAsync();
        (await editor.PostedOfTypeAsync("change")).ShouldBeEmpty();
        (await editor.PostedOfTypeAsync("action")).ShouldBeEmpty();
    }

    [Test]
    [Order(11)]
    public async Task SetScene_WhenTheReaderTypedThenTheAppReplacesTheText_ShouldNotLetUndoBringBackTheOldText()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Old text.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(" Mine.");
        await editor.PostedAsync("change");
        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", "Remote text from another device.\n", version: 2)));
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Remote text");
        await editor.PostedAsync("state", "m.canUndo === false");
        await editor.ClearPostedAsync();

        await editor.SendFromAppAsync(Command("undo"));

        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Remote text from another device.");
        await editor.FlushAsync();
        (await editor.PostedOfTypeAsync("change")).ShouldBeEmpty();
    }

    [Test]
    [Order(12)]
    public async Task SetScene_WhenTheAppInsertedABeat_ShouldNotLetUndoRemoveTheBeatLine()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(" More.");
        var beat = Beat(BeatId, "", "Walk the gallery.", 200);
        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", $"Alpha. More.\n\n::beat{{id=\"{BeatId}\"}}\n", beats: [beat], version: 2)));
        await Expect(editor.BeatInstruction(BeatId)).ToBeVisibleAsync();
        await editor.PostedAsync("state", "m.canUndo === false");
        await editor.ClearPostedAsync();

        await editor.SendFromAppAsync(Command("undo"));

        await Expect(editor.BeatInstruction(BeatId)).ToBeVisibleAsync();
        await editor.FlushAsync();
        (await editor.PostedOfTypeAsync("change")).ShouldBeEmpty();
    }

    [Test]
    [Order(13)]
    public async Task Load_WhenTypedTextIsStillWaiting_ShouldSendItBeforeReplacingTheScenes()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(" Kept.");

        await editor.SendFromAppAsync(Load(Scene("s9", "Nine", "Next.\n")));

        var change = await editor.PostedAsync("change");
        change.GetProperty("sceneId").GetString().ShouldBe("s1");
        change.GetProperty("markdown").GetString()!.ShouldContain("Alpha. Kept.");
    }

    [Test]
    [Order(14)]
    public async Task Load_WhenTheAppIsSplittingTheScene_ShouldDropTheTextTypedAfterTheSplitRequest()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(" Before.");
        await editor.SendFromAppAsync(Command("newScene"));
        await editor.PostedAsync("action");
        await Page.Keyboard.TypeAsync(" After.");

        await editor.SendFromAppAsync(Load(Scene("s1", "One", "Alpha. Before.\n"), Scene("s2", "Two", "\n")));
        await Expect(editor.SceneEditor("s2")).ToBeVisibleAsync();
        await editor.FlushAsync();

        var changes = await editor.PostedOfTypeAsync("change");
        changes.Count.ShouldBe(1);
        changes[0].GetProperty("markdown").GetString()!.ShouldNotContain("After.");
    }

    [Test]
    [Order(15)]
    public async Task Typing_WhenThePageIsHiddenOrLeft_ShouldSendTheWaitingChangeAtOnce()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(" Hidden.");

        await editor.HideAsync();

        var hidden = await editor.PostedAsync("change", "m.markdown.includes('Hidden.')");
        hidden.GetProperty("sceneId").GetString().ShouldBe("s1");
        await Page.Keyboard.TypeAsync(" Left.");
        await editor.FlushAsync();
        await editor.PostedAsync("change", "m.markdown.includes('Left.')");
    }

    [Test]
    [Order(16)]
    public async Task ContentHeight_WhenTheChapterIsDrawn_ShouldBePostedAndGrowWithTheContent()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "Alpha.\n")));
        var before = await editor.PostedAsync("contentHeight", "m.height > 0");

        await editor.SendFromAppAsync(SetScene(Scene("s1", "One", LongProse(), version: 2)));

        var tall = await editor.PostedAsync("contentHeight", $"m.height > {before.GetProperty("height").GetInt32() + 500}");
        tall.GetProperty("height").GetInt32().ShouldBeGreaterThan(before.GetProperty("height").GetInt32());
    }

    [Test]
    [Order(17)]
    public async Task Command_WhenScrollToMovedTheViewAwayFromTheFocusedScene_ShouldStillActOnTheFocusedScene()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n"), Scene("s2", "Two", "Second scene.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await editor.WaitForSelectionAsync("s1", "First scene.");
        await editor.SendFromAppAsync(ScrollTo("s2"));

        await editor.SendFromAppAsync(Command("bold"));

        var change = await editor.PostedAsync("change");
        change.GetProperty("sceneId").GetString().ShouldBe("s1");
        change.GetProperty("markdown").GetString()!.ShouldContain("**First scene.**");
    }

    private static string LongProse() => string.Join("\n\n", Enumerable.Range(1, 80).Select(n => $"Paragraph {n} of a long scene that fills the screen.")) + "\n";

    private async Task<MobileEditorPage> OpenWithAsync(object load)
    {
        var editor = new MobileEditorPage(Page);
        await editor.OpenAsync();
        await editor.PostedAsync(1);
        await editor.SendFromAppAsync(load);
        return editor;
    }
}
