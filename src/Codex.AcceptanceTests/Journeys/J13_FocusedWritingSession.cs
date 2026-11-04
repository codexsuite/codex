using System.Net.Http.Json;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Journeys;

// J13 · Focused writing session (#16 Part 2): scope the Write view to Chapter 6, set Format (narrow, indented
// paragraphs, summaries hidden), enter Focus and write with autosave running, leave with Esc and go back to the
// full manuscript.
[TestFixture, Category("J13")]
public class J13_FocusedWritingSession : JourneyTest
{
    private const string Typed = "Focused words ";

    private const string Ch6Counts = "640 words · 3 pages · 3m read";
    private const string Ch6CountsAfter = "642 words · 3 pages · 3m read";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Ch6 = SeedIds.Chapters.Ch6;
    private static readonly Guid Scene = SeedIds.Scenes.Ch6Sc1;
    private static readonly string ProsePath = $"/api/scenes/{Scene}/prose";

    private async Task<ProseDto> GetProseAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await client.GetFromJsonAsync<ProseDto>(ProsePath))!;
    }

    // Scope is view state held in the page (Format is the part kept per book), so each step that follows a reload picks
    // Chapter 6 again.
    private async Task<WritePageObject> OpenScopedToChapter6Async(string counts = Ch6Counts)
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        await write.ScopeSelect.SelectOptionAsync($"chapter:{Ch6}");
        await Expect(write.ScopeCounts).ToHaveTextAsync(counts);
        return write;
    }

    [Test, Order(1)]
    public async Task Step1_WhenScopeSetToChapter6_ShouldNarrowTheDocumentAndItsCounts()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.ScopeCounts).ToHaveTextAsync("13,703 words · 55 pages · 58m read");

        await write.ScopeSelect.SelectOptionAsync($"chapter:{Ch6}");

        await Expect(write.ScopeCounts).ToHaveTextAsync(Ch6Counts);
        await Expect(write.Scene(Scene)).ToBeVisibleAsync();
        await Expect(write.Scene(SeedIds.Scenes.Ch1Sc1)).ToHaveCountAsync(0);
        await Expect(write.Scene(SeedIds.Scenes.Ch5Sc1)).ToHaveCountAsync(0);
    }

    [Test, Order(2)]
    public async Task Step2_WhenFormatSetNarrowIndentedAndSummariesHidden_ShouldRestyleTheManuscript()
    {
        var write = await OpenScopedToChapter6Async();
        await Expect(write.SceneSummary(Scene)).ToBeVisibleAsync();

        await write.FormatButton.ClickAsync();
        await write.FormatOption("width", "narrow").ClickAsync();
        await write.FormatOption("paragraphs", "indent").ClickAsync();
        await write.FormatOption("summaries", "hide").ClickAsync();

        await Expect(write.Manuscript).ToHaveAttributeAsync("data-width", "narrow");
        await Expect(write.Manuscript).ToHaveAttributeAsync("data-paragraphs", "indent");
        await Expect(write.Manuscript).ToHaveAttributeAsync("data-hide-summaries", "true");
        await Expect(write.SceneSummary(Scene)).ToBeHiddenAsync();
    }

    [Test, Order(3)]
    public async Task Step3_WhenFocusEntered_ShouldHideEverythingButTheProseAndKeepAutosaving()
    {
        var bar = new BookBar(Page);
        var sidebar = new KbSidebarFrame(Page);
        var write = await OpenScopedToChapter6Async();
        await Expect(write.Manuscript).ToHaveAttributeAsync("data-width", "narrow");
        var before = await GetProseAsync();

        await write.FocusButton.ClickAsync();

        await Expect(write.FocusButton).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(sidebar.Root).ToBeHiddenAsync();
        await Expect(write.Minimap).ToBeHiddenAsync();
        await Expect(write.ProseEditor(Scene)).ToBeVisibleAsync();
        // The bar fades unless hovered or focused: park the pointer on the prose, away from it.
        await write.ProseEditor(Scene).HoverAsync();
        await Expect(bar.Root).ToHaveCSSAsync("opacity", "0.3");

        var response = await Page.RunAndWaitForApiAsync(ProsePath, "PUT", async () =>
        {
            await write.ProseEditor(Scene).ClickAsync();
            await Page.Keyboard.PressAsync("Control+Home");
            await Page.Keyboard.TypeAsync(Typed);
        });

        response.Ok.ShouldBeTrue();
        await Expect(write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");
        var after = await GetProseAsync();
        after.Version.ShouldBe(before.Version + 1);
        after.Body.ShouldStartWith(Typed);
        // The chapter's word count follows the two typed words once Focus is left (Step 4).
    }

    [Test, Order(4)]
    public async Task Step4_WhenEscPressedAndScopeSetToFullManuscript_ShouldRestoreTheChromeAndTheBookCounts()
    {
        var bar = new BookBar(Page);
        var sidebar = new KbSidebarFrame(Page);
        var write = await OpenScopedToChapter6Async(Ch6CountsAfter);
        await write.FocusButton.ClickAsync();
        await Expect(write.FocusButton).ToHaveAttributeAsync("aria-pressed", "true");

        await Page.Keyboard.PressAsync("Escape");

        await Expect(write.FocusButton).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(sidebar.Root).ToBeVisibleAsync();
        await Expect(write.Minimap).ToBeVisibleAsync();
        await Expect(bar.Root).ToHaveCSSAsync("opacity", "1");

        await write.ScopeSelect.SelectOptionAsync("book");

        await Expect(write.ScopeCounts).ToHaveTextAsync("13,705 words · 55 pages · 58m read");
        await Expect(write.Scene(SeedIds.Scenes.Ch1Sc1)).ToBeVisibleAsync();
    }
}
