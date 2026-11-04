namespace Codex.AcceptanceTests.Lanes.MobileEditor;

// The messages the app sends the page, built the way the bridge's `HostMessage` types are shaped
// (src/Codex.Core.Client/src/editing/editorBridge.ts).
public static class MobileEditorMessages
{
    public const int ProtocolVersion = 1;

    public static object Load(params object[] scenes) => LoadWith(scenes: scenes);

    public static object LoadWith(
        object[] scenes,
        object[]? entries = null,
        string theme = "light",
        string? scrollToSceneId = null,
        bool readOnly = false) => new
    {
        type = "load",
        v = ProtocolVersion,
        theme,
        density = "touch",
        scenes,
        entries = entries ?? [],
        scrollToSceneId,
        readOnly,
    };

    public static object Scene(
        string id,
        string title,
        string prose,
        string number = "1.1",
        string summary = "",
        string[]? labels = null,
        int words = 0,
        object[]? chips = null,
        object[]? beats = null,
        object[]? additions = null,
        int version = 1) => new
    {
        id,
        number,
        title,
        labels = labels ?? [],
        words,
        summary,
        chips = chips ?? [],
        beats = beats ?? [],
        additions = additions ?? [],
        prose,
        version,
    };

    public static object Chip(string id, string name, string category) => new { id, name, category };

    public static object Beat(string id, string title, string body, int? targetWordCount, bool hidden = false) =>
        new { id, title, body, targetWordCount, hidden };

    public static object Addition(string id, string entryId, string entryName, string body) =>
        new { id, entryId, entryName, body };

    public static object Entry(string id, string name, string category) =>
        new { id, name, category, aliases = Array.Empty<string>() };

    public static object Command(string command) => new { type = "command", command };

    public static object SetScene(object scene) => new { type = "setScene", scene };

    public static object SceneMeta(
        string sceneId,
        string title,
        string[] labels,
        int words,
        string summary,
        object[] chips) => new { type = "sceneMeta", sceneId, title, labels, words, summary, chips };

    public static object ScrollTo(string sceneId) => new { type = "scrollTo", sceneId };
}
