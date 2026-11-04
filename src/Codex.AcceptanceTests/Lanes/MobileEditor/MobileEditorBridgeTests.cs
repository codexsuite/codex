using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Shouldly;

namespace Codex.AcceptanceTests.Lanes.MobileEditor;

// The contract test for the page the app loads into its WebView: it announces itself and draws what `load` brings.
// Keep it green and extend it when the page grows.
[TestFixture]
public class MobileEditorBridgeTests : EmptyAcceptanceTest
{
    private const int ProtocolVersion = MobileEditorMessages.ProtocolVersion;

    [Test]
    [Order(1)]
    public async Task Page_WhenOpened_ShouldAnnounceItselfWithTheProtocolVersion()
    {
        var editor = new MobileEditorPage(Page);

        await editor.OpenAsync();

        var posted = await editor.PostedAsync(1);
        posted[0].GetProperty("type").GetString().ShouldBe("ready");
        posted[0].GetProperty("v").GetInt32().ShouldBe(ProtocolVersion);
    }

    [Test]
    [Order(2)]
    public async Task Page_WhenLoadArrives_ShouldDrawEachSceneTitleInOrder()
    {
        var editor = new MobileEditorPage(Page);
        await editor.OpenAsync();
        await editor.PostedAsync(1);

        await editor.SendFromAppAsync(Load(Scene("s1", "An Empty Seat", "The court waits.\n"), Scene("s2", "The Letter", "")));

        await Expect(editor.SceneTitle("s1")).ToHaveTextAsync("An Empty Seat");
        await Expect(editor.SceneTitle("s2")).ToHaveTextAsync("The Letter");
        await Expect(editor.SceneProse("s1")).ToContainTextAsync("The court waits.");
    }

    [Test]
    [Order(3)]
    public async Task Page_WhenLoadArrivesOnDocument_ShouldDrawItToo()
    {
        var editor = new MobileEditorPage(Page);
        await editor.OpenAsync();
        await editor.PostedAsync(1);

        await editor.SendFromAppAsync(Load(Scene("s1", "Android Title", "")), target: "document");

        await Expect(editor.SceneTitle("s1")).ToHaveTextAsync("Android Title");
    }

    [Test]
    [Order(4)]
    public async Task Page_WhenAMessageIsInvalid_ShouldReportAnError()
    {
        var editor = new MobileEditorPage(Page);
        await editor.OpenAsync();
        await editor.PostedAsync(1);

        await editor.SendFromAppAsync(new { type = "explode" });

        var posted = await editor.PostedAsync(2);
        posted[1].GetProperty("type").GetString().ShouldBe("error");
    }

    private static object Load(params object[] scenes) => MobileEditorMessages.Load(scenes);

    private static object Scene(string id, string title, string prose) => MobileEditorMessages.Scene(id, title, prose);
}
