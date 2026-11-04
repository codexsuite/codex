using Codex.AcceptanceTests.Harness;
using Shouldly;
using static Codex.AcceptanceTests.Lanes.MobileEditor.MobileEditorMessages;

namespace Codex.AcceptanceTests.Lanes.MobileEditor;

// The scene card above each scene's prose, and the read-only beat and addition lines inside it.
[TestFixture]
public class MobileEditorCardTests : EmptyAcceptanceTest
{
    private const string BeatId = "0199c0de-0000-7000-8000-000700000001";

    private const string AdditionId = "0199c0de-0000-7000-8000-000800000001";

    private static readonly object[] TwoScenes =
    [
        Scene(
            "s1",
            "An Empty Seat",
            "The court waits.\n",
            number: "1.1",
            summary: "Maren returns to find the throne claimed.",
            labels: ["Drafting"],
            words: 3860,
            chips: [Chip("maren", "Maren", "person")]),
        Scene(
            "s2",
            "The Letter",
            "A letter arrives.\n",
            number: "1.2",
            summary: "Hidden while the scene is not being edited.",
            labels: ["Research"],
            words: 2140,
            chips: [Chip("abbot", "The Abbot", "person")]),
    ];

    [Test]
    [Order(1)]
    public async Task Card_WhenSceneIsOpen_ShouldShowNumberTitleLabelsWordsSummaryAndChips()
    {
        var editor = await OpenWithAsync(LoadWith(TwoScenes));

        await Expect(editor.SceneNumber("s1")).ToHaveTextAsync("Scene 1");
        await Expect(editor.SceneTitle("s1")).ToHaveTextAsync("An Empty Seat");
        await Expect(editor.SceneLabel("s1", "Drafting")).ToBeVisibleAsync();
        await Expect(editor.SceneWords("s1")).ToHaveTextAsync("3,860 words");
        await Expect(editor.SceneSummary("s1")).ToHaveTextAsync("Maren returns to find the throne claimed.");
        await Expect(editor.SceneChip("s1", "maren")).ToHaveTextAsync("Maren");
    }

    [Test]
    [Order(2)]
    public async Task Card_WhenSceneIsNotTheOneBeingEdited_ShouldCollapseToOneLine()
    {
        var editor = await OpenWithAsync(LoadWith(TwoScenes));

        await Expect(editor.SceneNumber("s2")).ToHaveTextAsync("Scene 2");
        await Expect(editor.SceneTitle("s2")).ToHaveTextAsync("The Letter");
        await Expect(editor.SceneWords("s2")).ToHaveTextAsync("2,140 words");
        await Expect(editor.SceneSummary("s2")).ToHaveCountAsync(0);
        await Expect(editor.SceneChip("s2", "abbot")).ToHaveCountAsync(0);
        await Expect(editor.SceneLabel("s2", "Research")).ToHaveCountAsync(0);
    }

    [Test]
    [Order(3)]
    public async Task Card_WhenTheReaderScrollsAnotherSceneIntoView_ShouldOpenItsCardAndCollapseTheOther()
    {
        var editor = await OpenWithAsync(LoadWith(LongThenShort()));
        await Expect(editor.SceneSummary("s1")).ToBeVisibleAsync();

        await Page.Mouse.WheelAsync(0, 100_000);

        await Expect(editor.SceneSummary("s2")).ToBeVisibleAsync();
        await Expect(editor.SceneSummary("s1")).ToHaveCountAsync(0);
    }

    [Test]
    [Order(10)]
    public async Task Card_WhenASceneHasFocus_ShouldKeepEveryCardAsItWasUntilFocusLeaves()
    {
        var editor = await OpenWithAsync(LoadWith(LongThenShort()));
        await editor.SceneEditor("s1").ClickAsync();
        await Expect(editor.SceneSummary("s1")).ToBeVisibleAsync();

        await editor.SendFromAppAsync(ScrollTo("s2"));
        await Expect(editor.SceneCard("s2")).ToBeInViewportAsync();

        await Expect(editor.SceneSummary("s1")).ToBeVisibleAsync();
        await Expect(editor.SceneSummary("s2")).ToHaveCountAsync(0);

        await editor.SendFromAppAsync(Command("blur"));

        await Expect(editor.SceneSummary("s2")).ToBeVisibleAsync();
        await Expect(editor.SceneSummary("s1")).ToHaveCountAsync(0);
    }

    [Test]
    [Order(11)]
    public async Task Beat_WhenHidden_ShouldDrawOneLineRowWithTitleInstructionAndLength()
    {
        var beat = Beat(BeatId, "Gallery", "Tobin counts the **guards**.", 400, hidden: true);
        var editor = await OpenWithAsync(Load(Scene("s1", "Gallery", $"Before.\n\n::beat{{id=\"{BeatId}\"}}\n", beats: [beat])));

        await Expect(editor.BeatHidden(BeatId)).ToContainTextAsync("Beat");
        await Expect(editor.BeatHidden(BeatId)).ToContainTextAsync("Gallery");
        await Expect(editor.BeatHidden(BeatId)).ToContainTextAsync("Tobin counts the guards.");
        await Expect(editor.BeatHidden(BeatId)).ToContainTextAsync("400w");
        await Expect(editor.BeatHidden(BeatId)).Not.ToContainTextAsync("Show");
        await Expect(editor.Beat(BeatId)).ToHaveCountAsync(0);
    }

    [Test]
    [Order(12)]
    public async Task Beat_WhenHiddenHasNoTitle_ShouldReadUntitled()
    {
        var beat = Beat(BeatId, "", "Open on the bell.", null, hidden: true);
        var editor = await OpenWithAsync(Load(Scene("s1", "Gallery", $"::beat{{id=\"{BeatId}\"}}\n", beats: [beat])));

        await Expect(editor.BeatHidden(BeatId)).ToContainTextAsync("Untitled");
    }

    [Test]
    [Order(13)]
    public async Task SetScene_WhenOnlyTheBeatsHiddenFlagFlips_ShouldRedrawTheBeatWithoutTouchingTheProse()
    {
        var prose = $"Before.\n\n::beat{{id=\"{BeatId}\"}}\n";
        var editor = await OpenWithAsync(Load(Scene("s1", "Gallery", prose, beats: [Beat(BeatId, "", "Open on the bell.", 200)])));
        await Expect(editor.Beat(BeatId)).ToBeVisibleAsync();

        await editor.SendFromAppAsync(SetScene(Scene("s1", "Gallery", prose, beats: [Beat(BeatId, "", "Open on the bell.", 200, hidden: true)], version: 2)));

        await Expect(editor.BeatHidden(BeatId)).ToBeVisibleAsync();
        await Expect(editor.Beat(BeatId)).ToHaveCountAsync(0);

        await editor.SendFromAppAsync(SetScene(Scene("s1", "Gallery", prose, beats: [Beat(BeatId, "", "Open on the bell.", 200)], version: 3)));

        await Expect(editor.Beat(BeatId)).ToBeVisibleAsync();
    }

    [Test]
    [Order(14)]
    public async Task Directive_WhenTheSceneHasNoRowForIt_ShouldShowItsLabelAndNotTheRawId()
    {
        var prose = $"Text.\n\n::beat{{id=\"{BeatId}\"}}\n\n::kb-addition{{id=\"{AdditionId}\"}}\n";
        var editor = await OpenWithAsync(Load(Scene("s1", "Gallery", prose)));

        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Scene beat");
        await Expect(editor.SceneEditor("s1")).ToContainTextAsync("Codex addition");
        await Expect(editor.SceneEditor("s1")).Not.ToContainTextAsync(BeatId);
        await Expect(editor.SceneEditor("s1")).Not.ToContainTextAsync(AdditionId);
    }

    [Test]
    [Order(4)]
    public async Task Card_WhenTapped_ShouldAskTheAppToOpenTheScene()
    {
        var editor = await OpenWithAsync(LoadWith(TwoScenes));

        await editor.SceneCard("s2").ClickAsync();

        var opened = await editor.PostedAsync("openScene");
        opened.GetProperty("sceneId").GetString().ShouldBe("s2");
    }

    [Test]
    [Order(5)]
    public async Task Card_WhenSceneHasNoTitle_ShouldReadUntitled()
    {
        var editor = await OpenWithAsync(Load(Scene("s1", "", "Text.\n")));

        await Expect(editor.SceneTitle("s1")).ToHaveTextAsync("Untitled");
    }

    [Test]
    [Order(6)]
    public async Task Page_WhenLoadSaysDark_ShouldSetThemeAndTouchDensity()
    {
        var editor = await OpenWithAsync(LoadWith(TwoScenes, theme: "dark"));

        await Expect(editor.SceneCard("s1")).ToBeVisibleAsync();
        var theme = await Page.EvaluateAsync<string>("document.documentElement.dataset.theme");
        var density = await Page.EvaluateAsync<string>("document.documentElement.dataset.density");
        theme.ShouldBe("dark");
        density.ShouldBe("touch");
    }

    [Test]
    [Order(7)]
    public async Task Beat_WhenSceneHasOne_ShouldDrawItsInstructionAndLengthWithoutControls()
    {
        var prose = $"Before the beat.\n\n::beat{{id=\"{BeatId}\"}}\n\nAfter the beat.\n";
        var beat = Beat(BeatId, "", "Tobin counts the **guards**: eleven, then twelve.", 400);
        var editor = await OpenWithAsync(Load(Scene("s1", "Gallery", prose, beats: [beat])));

        await Expect(editor.Beat(BeatId)).ToBeVisibleAsync();
        await Expect(editor.BeatInstruction(BeatId)).ToHaveTextAsync("Tobin counts the guards: eleven, then twelve.");
        await Expect(editor.BeatLength(BeatId)).ToHaveTextAsync("400 words");
        await Expect(editor.Beat(BeatId)).Not.ToContainTextAsync("Generate");
    }

    [Test]
    [Order(8)]
    public async Task Beat_WhenItHasNoTargetLength_ShouldDrawNoLength()
    {
        var prose = $"::beat{{id=\"{BeatId}\"}}\n";
        var beat = Beat(BeatId, "Opening", "Open on the bell.", null);
        var editor = await OpenWithAsync(Load(Scene("s1", "Gallery", prose, beats: [beat])));

        await Expect(editor.Beat(BeatId)).ToContainTextAsync("Scene beat · Opening");
        await Expect(editor.BeatLength(BeatId)).ToHaveCountAsync(0);
    }

    [Test]
    [Order(9)]
    public async Task Addition_WhenSceneHasOne_ShouldDrawTheEntryAndNote()
    {
        var prose = $"Text.\n\n::kb-addition{{id=\"{AdditionId}\"}}\n";
        var addition = Addition(AdditionId, "maren", "Maren", "She limps on the left foot.");
        var editor = await OpenWithAsync(Load(Scene("s1", "Gallery", prose, additions: [addition])));

        await Expect(editor.Addition(AdditionId)).ToContainTextAsync("Codex addition · Maren");
        await Expect(editor.Addition(AdditionId)).ToContainTextAsync("She limps on the left foot.");
    }

    private static object[] LongThenShort() =>
    [
        Scene("s1", "One", LongProse("Paragraph"), number: "1.1", summary: "First summary."),
        Scene("s2", "Two", LongProse("Further paragraph"), number: "1.2", summary: "Second summary."),
    ];

    private static string LongProse(string lead) => string.Join("\n\n", Enumerable.Range(1, 80).Select(n => $"{lead} {n} of a long scene that fills the screen.")) + "\n";

    private async Task<MobileEditorPage> OpenWithAsync(object load)
    {
        var editor = new MobileEditorPage(Page);
        await editor.OpenAsync();
        await editor.PostedAsync(1);
        await editor.SendFromAppAsync(load);
        return editor;
    }
}
