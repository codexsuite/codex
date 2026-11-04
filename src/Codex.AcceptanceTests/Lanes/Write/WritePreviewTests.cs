using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Write;

// Write → Format → Preview (#13, J12 step 6): the Players Guide's chapter 1 paged beside the prose in its template, made
// again after an edit in Write saves, and remembered open or closed in this browser. The steps share one browser
// context, so what the first step stores the next ones read. Needs pandoc, as CI installs it.
[TestFixture]
public class WritePreviewTests : WriteLaneTest
{
    private static readonly string BookId = SeedIds.Books.PlayersGuide.ToString();
    private static readonly Guid FigureScene = SeedIds.Scenes.PlayersGuideFigureScene;
    private const string Sentence = "The pass was closed by the first snow.";

    private WritePageObject Write => new(Page);

    [Test, Order(1)]
    public async Task Preview_WhenShownFromFormat_ShouldPageTheChapterInTheBooksTemplate()
    {
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(Write.Manuscript).ToBeVisibleAsync();
        await Expect(Write.Preview).ToHaveCountAsync(0);

        await Write.ShowPreviewAsync();

        await Expect(Write.PreviewPages).ToHaveTextAsync(new Regex(@"^≈ [1-9]\d* pages?$"));
        await Expect(Write.PreviewCanvas).ToContainTextAsync("The Fallen World, as the players first see it.");
        await Expect(Write.PreviewTemplate).ToHaveValueAsync("novel-6x9");
        await Expect(Write.PreviewInLayout).ToHaveAttributeAsync("href", new Regex($"^{Regex.Escape(Links.Layout(BookId))}"));
    }

    [Test, Order(2)]
    public async Task Preview_WhenTheProseIsEdited_ShouldShowTheEditAfterTheSave()
    {
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(Write.PreviewPages).ToHaveTextAsync(new Regex(@"^≈ [1-9]\d* pages?$"));
        await Expect(Write.PreviewCanvas).Not.ToContainTextAsync(Sentence);

        await Write.PlaceCaretAtStartAsync(FigureScene);
        await Page.Keyboard.TypeAsync(Sentence + " ");
        await Write.Manuscript.FocusAsync();
        await Expect(Write.SaveStatus).ToHaveAttributeAsync("data-state", "saved");

        await Expect(Write.PreviewCanvas).ToContainTextAsync(Sentence, new() { Timeout = 3000 });
    }

    [Test, Order(3)]
    public async Task Preview_WhenClosed_ShouldStayClosedOnTheNextVisit()
    {
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(Write.Preview).ToBeVisibleAsync();

        await Write.PreviewClose.ClickAsync();
        await Expect(Write.Preview).ToHaveCountAsync(0);

        await Page.ReloadAsync();
        await Expect(Write.Manuscript).ToBeVisibleAsync();
        await Expect(Write.Preview).ToHaveCountAsync(0);
    }
}
