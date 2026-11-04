using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Write;

// "Updated elsewhere" on Write: a save from another session (a request without this page's client id) puts the
// banner up; saving another scene meanwhile does not take it down; Reload reads the changed scene again and clears it.
[TestFixture]
public class WriteRemoteReloadTests : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Changed = SeedIds.Scenes.Ch5Sc1;
    private static readonly Guid Typed = SeedIds.Scenes.Ch3Sc1;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private const string Elsewhere = "Written in another session.";

    [Test]
    [CancelAfter(90000)]
    public async Task Reload_WhenAnotherSessionChangedAScene_ShouldKeepTheBannerUntilReloadThenShowTheirText()
    {
        var write = new WritePageObject(Page);
        var joined = WatchJoinAsync(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(Changed)).ToBeVisibleAsync();
        await Within(joined, "the page to join the book");

        using var other = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var prose = (await other.GetFromJsonAsync<ProseDto>($"/api/scenes/{Changed}/prose", JsonSerializerOptions.Web))!;
        var put = await other.PutAsJsonAsync($"/api/scenes/{Changed}/prose", new SaveProseRequest($"{Elsewhere}\n\n{prose.Body}", prose.Version));
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "remoteChanged");

        var saved = await Page.RunAndWaitForApiAsync($"/api/scenes/{Typed}/prose", "PUT", async () =>
        {
            await write.ProseEditor(Typed).ClickAsync();
            await Page.Keyboard.PressAsync("Control+Home");
            await Page.Keyboard.TypeAsync("Typed here. ");
            await write.Manuscript.FocusAsync();
        });
        saved.Status.ShouldBe((int)HttpStatusCode.OK);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "remoteChanged");
        await Expect(write.ProseEditor(Changed)).Not.ToContainTextAsync(Elsewhere);

        await write.SaveStatusReload.ClickAsync();

        await Expect(write.ProseEditor(Changed)).ToContainTextAsync(Elsewhere);
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        await Expect(write.ProseEditor(Typed)).ToContainTextAsync("Typed here.");
    }

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

    // Resolves once the page's JoinBook has been answered (as in Lanes/Realtime/RealtimeTests), so the save made
    // afterwards is certain to reach it.
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
}
