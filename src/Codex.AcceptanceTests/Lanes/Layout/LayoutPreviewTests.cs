using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Layout;
using Codex.Contracts;
using Codex.Contracts.Manuscript;

namespace Codex.AcceptanceTests.Lanes.Layout;

// Quick preview on the seed's Players Guide chapter 1 (a low-dpi figure and a raw-LaTeX block): the pages render,
// an edit made elsewhere re-flows them within three seconds, and the print issues are marked on the page.
// Needs pandoc on PATH, as CI installs it.
[TestFixture]
public class LayoutPreviewTests : SeededAcceptanceTest
{
    private static readonly string BookId = SeedIds.Books.PlayersGuide.ToString();
    private static readonly Guid FigureScene = SeedIds.Scenes.PlayersGuideFigureScene;
    private const string Sentence = "The Hollow Crown was never found by the cartographers.";

    private LayoutScreen Layout => new(Page);

    // Each test gets its own page, so each opens the screen; the steps share the database.
    private async Task OpenAsync()
    {
        await Page.GotoAsync(Links.Layout(BookId));
        await Expect(Layout.StatusPages).ToHaveTextAsync(new Regex(@"^[1-9]\d* pages?$"));
    }

    [Test, Order(1)]
    public async Task Layout_WhenOpened_ShouldRenderTheFirstChapterAsPages()
    {
        await OpenAsync();

        await Expect(Layout.RailPage(1)).ToBeVisibleAsync();
        await Expect(Layout.Canvas).ToContainTextAsync("The Fallen World, as the players first see it.");
        await Expect(Layout.TemplateSelect).ToHaveValueAsync("novel-6x9");
    }

    [Test, Order(2)]
    public async Task Layout_WhenAFigureIsBelow300Dpi_ShouldFlagItOnThePageAndListItInIssues()
    {
        await OpenAsync();
        await Expect(Layout.DpiFlags).ToHaveCountAsync(1);
        await Expect(Layout.DpiFlags.First).ToContainTextAsync("dpi");
        await Expect(Layout.LatexMarks).ToHaveCountAsync(1);

        await Layout.Tab("issues").ClickAsync();
        await Expect(Layout.IssueItems.Filter(new() { HasText = "Low resolution" })).ToHaveCountAsync(1);
        await Expect(Layout.IssueItems.Filter(new() { HasText = "Raw LaTeX" })).ToHaveCountAsync(1);

        await Layout.IssuesToggle.UncheckAsync();
        await Expect(Layout.DpiFlags.First).ToBeHiddenAsync();
        await Layout.IssuesToggle.CheckAsync();
        await Expect(Layout.DpiFlags.First).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Layout_WhenProseIsSavedElsewhere_ShouldReflowThePagesWithinThreeSeconds()
    {
        await OpenAsync();
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var current = (await api.GetFromJsonAsync<ProseDto>($"/api/scenes/{FigureScene}/prose"))!;
        await Expect(Layout.Canvas).Not.ToContainTextAsync(Sentence);

        using var saved = await api.PutAsJsonAsync($"/api/scenes/{FigureScene}/prose", new SaveProseRequest($"{Sentence}\n\n{current.Body}", current.Version));
        saved.EnsureSuccessStatusCode();

        await Expect(Layout.Canvas).ToContainTextAsync(Sentence, new() { Timeout = 3000 });
        await Expect(Layout.DpiFlags).ToHaveCountAsync(1);
    }

    [Test, Order(4)]
    public async Task Layout_WhenAnotherTemplateIsPicked_ShouldReflowTheSameTextAndKeepTheChoice()
    {
        await OpenAsync();
        await Layout.TemplateSelect.SelectOptionAsync("fantasy-rpg");

        await Expect(Layout.TrimSelect).ToHaveValueAsync("us-letter");
        await Expect(Layout.Canvas).ToContainTextAsync(Sentence);
        await Expect(Layout.StatusPages).ToHaveTextAsync(new Regex(@"^[1-9]\d* pages?$"));

        await OpenAsync();
        await Expect(Layout.TemplateSelect).ToHaveValueAsync("fantasy-rpg");
        await Expect(Layout.Canvas).ToContainTextAsync(Sentence);
    }

    [Test, Order(5)]
    public async Task Layout_WhenABlockIsClicked_ShouldOpenItsCardWithOpenInWrite()
    {
        await OpenAsync();
        await Layout.DpiFlags.First.ClickAsync();

        await Expect(Layout.BlockCard).ToContainTextAsync("fallen-world-map.png");
        await Expect(Layout.BlockCard).ToContainTextAsync("480 × 320");
        await Expect(Layout.OpenInWrite).ToBeVisibleAsync();
    }
}
