namespace Codex.AcceptanceTests.Harness;

// Waiting helpers. Tests wait for a locator or a network response, never for time.
public static class Waits
{
    // Runs the action (a click, a navigation) and returns the app's API response to the matching request.
    // Example: page.RunAndWaitForApiAsync("/api/books", "GET", () => page.GotoAsync("/")).
    // Matches the request path exactly, ignoring the query string.
    public static Task<IResponse> RunAndWaitForApiAsync(this IPage page, string path, string method, Func<Task> action) =>
        page.RunAndWaitForResponseAsync(
            action,
            r => string.Equals(r.Request.Method, method, StringComparison.OrdinalIgnoreCase)
                && new Uri(r.Url).AbsolutePath == path);

    // After a click in a ProseMirror editor (prose, summary): ProseMirror writes the clicked selection back to the DOM in
    // a zero-delay timer after mouseup, and on a busy runner a key pressed first (Control+End) is undone by it. A timer
    // queued now runs after that one. Not a wait for time: the delay is zero.
    // Example: await editor.ClickAsync(); await page.AfterEditorClickAsync(); await page.Keyboard.PressAsync("Control+End").
    public static Task AfterEditorClickAsync(this IPage page) =>
        page.EvaluateAsync("() => new Promise(resolve => setTimeout(resolve))");
}
