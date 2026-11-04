using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Lanes.Realtime;

// Two pages on the same scene: a save in one shows "Updated elsewhere · Reload" in the other, never in itself.
[TestFixture]
public class RealtimeTests : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid SceneId = SeedIds.Scenes.Ch5Sc1;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly string ProsePath = $"/api/scenes/{SceneId}/prose";

    [Test]
    [Order(1)]
    [CancelAfter(90000)]
    public async Task Save_WhenAnotherPageHasTheScene_ShouldShowUpdatedElsewhereOnlyOnTheOtherPage()
    {
        var saver = Page;
        var saverJoined = WatchJoinAsync(saver);
        var saverEvent = WatchEventAsync(saver);
        var saverWrite = new WritePageObject(saver);
        await saver.GotoAsync(Links.Write(BookId));
        await Expect(saverWrite.ProseEditor(SceneId)).ToBeVisibleAsync();
        // Its own context, so the two pages are two windows: a second tab of one window is hidden, and a hidden page
        // does not draw the editor.
        await using var otherWindow = new Window(await saver.Context.Browser!.NewContextAsync(
            new BrowserNewContextOptions { BaseURL = BaseUrl, ViewportSize = new ViewportSize { Width = 1280, Height = 800 }, TimezoneId = "UTC" }));
        var watcher = await otherWindow.Context.NewPageAsync();
        var watcherJoined = WatchJoinAsync(watcher);
        var watcherWrite = new WritePageObject(watcher);
        await watcher.GotoAsync(Links.Write(BookId));
        await Expect(watcherWrite.ProseEditor(SceneId)).ToBeVisibleAsync();
        await Within(Task.WhenAll(saverJoined, watcherJoined), "both pages to join the book");

        var response = await saver.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await saverWrite.ProseEditor(SceneId).ClickAsync();
            await saver.Keyboard.PressAsync("Control+Home");
            await saver.Keyboard.TypeAsync("Saved on the first page. ");
            await saverWrite.Manuscript.FocusAsync();
        });

        response.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(watcherWrite.SaveStatus).ToHaveAttributeAsync("data-state", "remoteChanged");
        await Expect(watcherWrite.SaveStatus).ToContainTextAsync("Updated elsewhere");
        await Expect(watcherWrite.SaveStatusReload).ToBeVisibleAsync();
        await Within(saverEvent, "the saving page to receive its own ProseSaved");
        await Expect(saverWrite.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(saverWrite.SaveStatusReload).ToHaveCountAsync(0);
    }

    // A write the page itself asks the server for, which rewrites a scene's prose and publishes ProseSaved: deleting a
    // mentioned KB entry (Kent Beck is mentioned in scene 3.1). The event names this page's own client id, so the page
    // must not call it "Updated elsewhere", whichever of the event and the response arrives first.
    [Test]
    [Order(2)]
    public async Task KbDelete_WhenPageRewritesItsOwnProse_ShouldNotShowUpdatedElsewhere()
    {
        var write = new WritePageObject(Page);
        var window = new KbEntryWindow(Page);
        var joined = WatchJoinAsync(Page);
        var saved = WatchEventAsync(Page);
        var entry = SeedIds.KbEntries.KentBeck;
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("kb", entry.ToString()), ("entry", entry.ToString()))));
        await Expect(write.ProseEditor(SeedIds.Scenes.Ch3Sc1)).ToBeVisibleAsync();
        await Expect(window.Title).ToHaveTextAsync("Kent Beck");
        await Within(joined, "the page to join the book");
        // Let the server finish the delete, but hand its response to the page only after the page has received the
        // ProseSaved it caused: the page then judges the event with nothing from the response yet, so only the client
        // id can tell it the write is its own.
        // The book bar's status; the open KB entry window has a save status of its own.
        var barStatus = new BookBar(Page).SaveStatusSlot.GetByTestId(WriteTestIds.SaveStatus);
        string? stateWhileHeld = null;
        await Page.RouteAsync($"**/api/kb/entries/{entry}", async route =>
        {
            if (!string.Equals(route.Request.Method, "DELETE", StringComparison.OrdinalIgnoreCase))
            {
                await route.FallbackAsync();
                return;
            }
            var response = await route.FetchAsync();
            await Within(saved, "the page to receive the ProseSaved its delete caused");
            // Let the page's own task queue drain (the socket message is handled first), then read the bar. Later the
            // editors re-adopt the new prose and repaint the bar, so a banner would not last until the end.
            try
            {
                await Page.EvaluateAsync("() => new Promise(resolve => setTimeout(resolve))");
                stateWhileHeld = await barStatus.GetAttributeAsync("data-state");
            }
            catch (Exception e)
            {
                stateWhileHeld = "ERR " + e.Message;
            }
            await route.FulfillAsync(new RouteFulfillOptions { Response = response });
        });

        await window.Menu.ClickAsync();
        await window.MenuDelete.ClickAsync();
        await window.DeleteConfirm.ClickAsync();

        await Expect(window.Toast).ToContainTextAsync("Deleted Kent Beck");
        stateWhileHeld.ShouldBe("saved");
        await Expect(barStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(barStatus.GetByTestId(WriteTestIds.SaveStatusReload)).ToHaveCountAsync(0);
    }

    // Fails the test when `task` has not finished Patience after this call (not after the test started).
    private static async Task Within(Task task, string what)
    {
        using var timeout = new CancellationTokenSource();
        if (await Task.WhenAny(task, Task.Delay(Patience, timeout.Token)) != task)
        {
            throw new TimeoutException($"Timed out waiting for {what}.");
        }
        await timeout.CancelAsync();
        await task;
    }

    // Resolves once the page's first hub invocation (JoinBook, id 0 for the JS client) has been answered, so a save made afterwards is
    // certain to reach it. Registered before navigating, because the socket opens during page load.
    private static Task WatchJoinAsync(IPage page)
    {
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.WebSocket += (_, socket) => socket.FrameReceived += (_, frame) =>
        {
            if (frame.Text?.Contains("\"type\":3", StringComparison.Ordinal) == true
                && frame.Text.Contains("\"invocationId\":\"0\"", StringComparison.Ordinal))
            {
                joined.TrySetResult();
            }
        };
        return joined.Task;
    }

    // Resolves once the page's socket has received a ProseSaved, so "no banner" is asserted after it was delivered.
    private static Task WatchEventAsync(IPage page)
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.WebSocket += (_, socket) => socket.FrameReceived += (_, frame) =>
        {
            if (frame.Text?.Contains("\"type\":\"proseSaved\"", StringComparison.Ordinal) == true)
            {
                received.TrySetResult();
            }
        };
        return received.Task;
    }

    private sealed class Window(IBrowserContext context) : IAsyncDisposable
    {
        public IBrowserContext Context => context;

        public ValueTask DisposeAsync() => new(context.CloseAsync());
    }
}
