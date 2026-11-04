using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Shouldly;
using static Codex.AcceptanceTests.Lanes.MobileEditor.MobileEditorMessages;

namespace Codex.AcceptanceTests.Lanes.MobileEditor;

// Typing and the accessory bar's commands: what the page reports (`change`, `focus`, `blur`, `state`, `action`) and what a
// `command` does to the scene being edited.
[TestFixture]
public class MobileEditorEditingTests : EmptyAcceptanceTest
{
    private const string Maren = "0199c0de-0000-7000-8000-000b0000000b";

    private static readonly object[] Entries = [Entry(Maren, "Maren", "person"), Entry("0199c0de-0000-7000-8000-000b0000000c", "The Abbot", "person")];

    [Test]
    [Order(1)]
    public async Task Editor_WhenLoadArrives_ShouldDrawAnEditableEditorPerSceneWithItsProse()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n"), Scene("s2", "Two", "Second scene.\n")));

        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("First scene.");
        await Expect(editor.SceneEditor("s2")).ToContainTextAsync("Second scene.");
        await Expect(editor.SceneEditor("s1")).ToHaveAttributeAsync("contenteditable", "true");
    }

    [Test]
    [Order(2)]
    public async Task Typing_WhenThePauseEnds_ShouldPostOneChangeWithTheSceneMarkdown()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));

        await TypeAtEndAsync(editor, "s1", " More words.");

        var change = await editor.PostedAsync("change");
        change.GetProperty("sceneId").GetString().ShouldBe("s1");
        change.GetProperty("markdown").GetString()!.ShouldContain("First scene. More words.");
        (await editor.PostedOfTypeAsync("change")).Count.ShouldBe(1);
    }

    [Test]
    [Order(3)]
    public async Task Load_WhenTheEditorsMount_ShouldNotPostAChange()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("First scene.");

        // A change from the mount would be waiting to be sent: the flush point sends it.
        await editor.FlushAsync();

        (await editor.PostedOfTypeAsync("change")).ShouldBeEmpty();
    }

    [Test]
    [Order(4)]
    public async Task Focus_WhenTheReaderTapsAScene_ShouldPostFocusAndTheEditorState()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n"), Scene("s2", "Two", "Second scene.\n")));

        await editor.SceneEditor("s2").ClickAsync();

        var focus = await editor.PostedAsync("focus");
        focus.GetProperty("sceneId").GetString().ShouldBe("s2");
        var state = await editor.PostedAsync("state");
        state.GetProperty("bold").GetBoolean().ShouldBeFalse();
        state.GetProperty("canUndo").GetBoolean().ShouldBeFalse();
    }

    [Test]
    [Order(5)]
    public async Task BlurCommand_WhenTheEditorHasFocus_ShouldPostTheTypedTextThenBlur()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await TypeAtEndAsync(editor, "s1", " Quick.");

        await editor.SendFromAppAsync(Command("blur"));

        var blur = await editor.PostedAsync("blur");
        blur.GetProperty("sceneId").GetString().ShouldBe("s1");
        var posted = await editor.AllPostedAsync();
        IndexOf(posted, "change").ShouldBeLessThan(IndexOf(posted, "blur"));
    }

    [Test]
    [Order(6)]
    public async Task BoldCommand_WhenTextIsSelected_ShouldBoldItAndReportBoldActive()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await editor.WaitForSelectionAsync("s1", "First scene.");

        await editor.SendFromAppAsync(Command("bold"));

        var state = await editor.PostedAsync("state", "m.bold === true");
        state.GetProperty("italic").GetBoolean().ShouldBeFalse();
        var change = await editor.PostedAsync("change");
        change.GetProperty("markdown").GetString()!.ShouldContain("**First scene.**");
    }

    [Test]
    [Order(7)]
    public async Task ItalicAndQuoteCommands_WhenRun_ShouldReportTheirStateActive()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await editor.WaitForSelectionAsync("s1", "First scene.");

        await editor.SendFromAppAsync(Command("italic"));
        await editor.PostedAsync("state", "m.italic === true");
        await editor.SendFromAppAsync(Command("quote"));

        await editor.PostedAsync("state", "m.quote === true");
        var change = await editor.PostedAsync("change", "m.markdown.includes('> ')");
        change.GetProperty("markdown").GetString()!.ShouldContain("*First scene.*");
    }

    [Test]
    [Order(8)]
    public async Task UndoAndRedoCommands_WhenTextWasTyped_ShouldRevertAndRestoreItAndReportAvailability()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await TypeAtEndAsync(editor, "s1", " Extra.");
        await editor.PostedAsync("state", "m.canUndo === true");

        await editor.SendFromAppAsync(Command("undo"));

        await Expect(editor.SceneEditor("s1")).Not.ToContainTextAsync("Extra.");
        await editor.PostedAsync("state", "m.canRedo === true");

        await editor.SendFromAppAsync(Command("redo"));

        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Extra.");
    }

    [Test]
    [Order(9)]
    public async Task SlashCommand_WhenRun_ShouldOpenTheSlashMenuAtTheCursor()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await editor.SceneEditor("s1").ClickAsync();

        await editor.SendFromAppAsync(Command("slash"));

        await Expect(editor.SlashMenu).ToBeVisibleAsync();
        await Expect(editor.SlashItem("scene-beat")).ToBeVisibleAsync();
        await Expect(editor.SlashItem("block-quote")).ToBeVisibleAsync();
    }

    [Test]
    [Order(10)]
    public async Task MentionCommand_WhenRun_ShouldOfferEntriesAndInsertTheChosenMention()
    {
        var editor = await OpenWithAsync(LoadWith([Scene("s1", "One", "First scene.\n")], entries: Entries));
        await editor.SceneEditor("s1").ClickAsync();

        await editor.SendFromAppAsync(Command("mention"));

        await Expect(editor.MentionPicker).ToBeVisibleAsync();
        await editor.MentionOption(Maren).ClickAsync();
        var change = await editor.PostedAsync("change");
        change.GetProperty("markdown").GetString()!.ShouldContain($"[Maren](kb:{Maren})");
    }

    [Test]
    [Order(11)]
    public async Task BeatCommand_WhenRun_ShouldSendTheTypedTextFirstThenTheSceneWithABeatAtTheCursor()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await TypeAtEndAsync(editor, "s1", " Typed.");

        await editor.SendFromAppAsync(Command("beat"));

        var action = await editor.PostedAsync("action");
        action.GetProperty("action").GetString().ShouldBe("beat");
        action.GetProperty("sceneId").GetString().ShouldBe("s1");
        var beatId = action.GetProperty("beatId").GetString()!;
        beatId.ShouldMatch("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");
        var body = action.GetProperty("body").GetString()!;
        body.ShouldContain("First scene. Typed.");
        body.ShouldContain($"::beat{{id=\"{beatId}\"}}");
        var posted = await editor.AllPostedAsync();
        IndexOf(posted, "change").ShouldBeLessThan(IndexOf(posted, "action"));
    }

    [Test]
    [Order(12)]
    public async Task SlashMenuSceneBeat_WhenChosen_ShouldSendTheSameBeatAction()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await editor.SendFromAppAsync(Command("slash"));

        await editor.SlashItem("scene-beat").ClickAsync();

        var action = await editor.PostedAsync("action");
        action.GetProperty("action").GetString().ShouldBe("beat");
        action.GetProperty("body").GetString()!.ShouldContain("::beat{id=");
    }

    [Test]
    [Order(13)]
    public async Task NewSceneCommand_WhenTheCursorEndsTheFirstParagraph_ShouldSendTheHeadAndTail()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First paragraph.\n\nSecond paragraph.\n")));
        await editor.SceneEditor("s1").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+Home");
        await Page.Keyboard.PressAsync("End");
        await editor.WaitForCaretAsync("s1", "First paragraph.", "First paragraph.".Length);

        await editor.SendFromAppAsync(Command("newScene"));

        var action = await editor.PostedAsync("action");
        action.GetProperty("action").GetString().ShouldBe("newScene");
        var head = action.GetProperty("headBody").GetString()!;
        var tail = action.GetProperty("tailBody").GetString()!;
        head.ShouldContain("First paragraph.");
        head.ShouldNotContain("Second");
        tail.ShouldContain("Second paragraph.");
        tail.ShouldNotContain("First");
    }

    [Test]
    [Order(14)]
    public async Task Command_WhenAnotherSceneWasFocused_ShouldActOnThatScene()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "One", "First scene.\n"), Scene("s2", "Two", "Second scene.\n")));
        await editor.SceneEditor("s2").ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await editor.WaitForSelectionAsync("s2", "Second scene.");

        await editor.SendFromAppAsync(Command("bold"));

        var change = await editor.PostedAsync("change");
        change.GetProperty("sceneId").GetString().ShouldBe("s2");
        change.GetProperty("markdown").GetString()!.ShouldContain("**Second scene.**");
    }

    private async Task<MobileEditorPage> OpenWithAsync(object load)
    {
        var editor = new MobileEditorPage(Page);
        await editor.OpenAsync();
        await editor.PostedAsync(1);
        await editor.SendFromAppAsync(load);
        return editor;
    }

    private async Task TypeAtEndAsync(MobileEditorPage editor, string sceneId, string text)
    {
        await Expect(editor.SceneEditor(sceneId)).ToBeVisibleAsync();
        await editor.SceneEditor(sceneId).ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+End");
        await Page.Keyboard.TypeAsync(text);
    }

    private static int IndexOf(IReadOnlyList<JsonElement> posted, string type)
    {
        for (var i = 0; i < posted.Count; i++)
        {
            if (posted[i].GetProperty("type").GetString() == type) return i;
        }

        return -1;
    }
}
