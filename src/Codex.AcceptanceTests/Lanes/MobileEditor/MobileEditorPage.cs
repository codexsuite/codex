using System.Text.Json;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Lanes.MobileEditor;

// The mobile editor page as the app's WebView sees it: opened by URL, with `window.ReactNativeWebView` provided by an
// init script (what react-native-webview injects), the app's messages delivered as `message` events on `window`, and
// the page's messages collected in `window.__posted`. The protocol is src/Codex.Core.Client/src/editing/editorBridge.ts.
public sealed class MobileEditorPage(IPage page)
{
    private const string Url = "/mobile-editor.html";

    private const string FakeWebViewScript = """
        window.__posted = [];
        window.ReactNativeWebView = { postMessage: (message) => window.__posted.push(JSON.parse(message)) };
        """;

    public async Task OpenAsync()
    {
        await page.AddInitScriptAsync(FakeWebViewScript);
        await page.GotoAsync(Url);
    }

    public ILocator Root => page.GetByTestId(MobileEditorTestIds.Root);

    public ILocator SceneTitle(string sceneId) => page.GetByTestId(MobileEditorTestIds.SceneTitle(sceneId));

    public ILocator SceneProse(string sceneId) => page.GetByTestId(MobileEditorTestIds.SceneProse(sceneId));

    public ILocator SceneEditor(string sceneId) => page.GetByTestId(MobileEditorTestIds.SceneEditor(sceneId));

    public ILocator SceneCard(string sceneId) => page.GetByTestId(MobileEditorTestIds.SceneCard(sceneId));

    public ILocator SceneNumber(string sceneId) => page.GetByTestId(MobileEditorTestIds.SceneNumber(sceneId));

    public ILocator SceneWords(string sceneId) => page.GetByTestId(MobileEditorTestIds.SceneWords(sceneId));

    public ILocator SceneSummary(string sceneId) => page.GetByTestId(MobileEditorTestIds.SceneSummary(sceneId));

    public ILocator SceneLabel(string sceneId, string label) => page.GetByTestId(MobileEditorTestIds.SceneLabel(sceneId, label));

    public ILocator SceneChip(string sceneId, string entryId) => page.GetByTestId(MobileEditorTestIds.SceneChip(sceneId, entryId));

    public ILocator Beat(string beatId) => page.GetByTestId(MobileEditorTestIds.Beat(beatId));

    public ILocator BeatInstruction(string beatId) => page.GetByTestId(MobileEditorTestIds.BeatInstruction(beatId));

    public ILocator BeatHidden(string beatId) => page.GetByTestId(MobileEditorTestIds.BeatHidden(beatId));

    public ILocator BeatLength(string beatId) => page.GetByTestId(MobileEditorTestIds.BeatLength(beatId));

    public ILocator Addition(string additionId) => page.GetByTestId(MobileEditorTestIds.Addition(additionId));

    public ILocator SlashMenu => page.GetByTestId(MobileEditorTestIds.SlashMenu);

    public ILocator MentionPicker => page.GetByTestId(MobileEditorTestIds.MentionPicker);

    public ILocator MentionOption(string entryId) => page.GetByTestId(MobileEditorTestIds.MentionOption(entryId));

    public ILocator SlashItem(string itemId) => page.GetByTestId(MobileEditorTestIds.SlashItem(itemId));

    // Waits until the browser's caret is collapsed at `offset` in the text node holding exactly `text`, inside the scene's
    // editor. Keyboard navigation lands asynchronously (the editor learns of it from `selectionchange`), so a test that
    // places the cursor with keys waits here before it sends a command or a `setScene` that reads the cursor.
    public Task WaitForCaretAsync(string sceneId, string text, int offset) =>
        page.WaitForFunctionAsync(
            """
            ([testId, text, offset]) => {
              const selection = window.getSelection();
              const editor = document.querySelector(`[data-testid="${testId}"]`);
              const node = selection?.anchorNode;
              return !!selection && selection.isCollapsed && !!editor && !!node && editor.contains(node)
                && node.nodeType === Node.TEXT_NODE && node.data === text && selection.anchorOffset === offset;
            }
            """,
            new object[] { MobileEditorTestIds.SceneEditor(sceneId), text, offset });

    // Waits until the browser's selection covers exactly `text` inside the scene's editor (after select-all), for the same
    // reason as `WaitForCaretAsync`.
    public Task WaitForSelectionAsync(string sceneId, string text) =>
        page.WaitForFunctionAsync(
            """
            ([testId, text]) => {
              const selection = window.getSelection();
              const editor = document.querySelector(`[data-testid="${testId}"]`);
              return !!selection && !!editor && !!selection.anchorNode && editor.contains(selection.anchorNode)
                && selection.toString() === text;
            }
            """,
            new object[] { MobileEditorTestIds.SceneEditor(sceneId), text });

    // Sends the app's message to the page, the way WebView.postMessage does on iOS (window) or Android (document).
    public async Task SendFromAppAsync(object message, string target = "window")
    {
        var json = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
        await page.EvaluateAsync(
            "([target, json]) => (target === 'document' ? document : window).dispatchEvent(new MessageEvent('message', { data: json }))",
            new object[] { target, json });
    }

    // Waits until the page has posted `count` messages, then returns them all (each as a JSON object).
    public async Task<IReadOnlyList<JsonElement>> PostedAsync(int count)
    {
        await page.WaitForFunctionAsync("count => window.__posted.length >= count", count);
        return await AllPostedAsync();
    }

    // Waits until the page has posted a message of `type` for which `predicate` (a JS expression over `m`) holds, then
    // returns the first such message.
    public async Task<JsonElement> PostedAsync(string type, string predicate = "true")
    {
        var find = $"(type) => window.__posted.find((m) => m.type === type && ({predicate}))";
        await page.WaitForFunctionAsync($"type => !!({find})(type)", type);
        return await page.EvaluateAsync<JsonElement>(find, type);
    }

    public async Task<IReadOnlyList<JsonElement>> AllPostedAsync()
    {
        var posted = await page.EvaluateAsync<JsonElement>("window.__posted");
        return posted.EnumerateArray().ToList();
    }

    public async Task<IReadOnlyList<JsonElement>> PostedOfTypeAsync(string type) =>
        (await AllPostedAsync()).Where(m => m.GetProperty("type").GetString() == type).ToList();

    // The app is leaving the page (a flush point): everything typed and not yet sent is sent now.
    public Task FlushAsync() => page.EvaluateAsync("window.dispatchEvent(new Event('pagehide'))");

    // The WebView goes to the background: `document.visibilityState` becomes hidden.
    public Task HideAsync() => page.EvaluateAsync(
        "Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true }); document.dispatchEvent(new Event('visibilitychange'))");

    // Forgets what was posted so far, so the next wait sees only what follows.
    public Task ClearPostedAsync() => page.EvaluateAsync("window.__posted.length = 0");
}
