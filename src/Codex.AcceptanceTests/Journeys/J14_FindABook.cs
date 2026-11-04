using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.Contracts;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Journeys;

// J14 · Find a book (#16 Part 2): get to Undertow's Plan from the Library by search, by the list view grouped by
// series, and by the command palette. Step 4's "from anywhere" is the global Ctrl/Cmd+K hotkey (accepted deviation).
[TestFixture, Category("J14")]
public class J14_FindABook : JourneyTest
{
    private static readonly string Undertow = SeedIds.Books.Undertow.ToString();
    private static readonly string UndertowPlan = $"/books/{SeedIds.Books.Undertow}/plan";

    private LibraryScreen Library => new(Page);

    [Test, Order(1)]
    public async Task Step1_WhenUnderSearchedInTheLibrary_ShouldKeepTidewaterAndEmptyTheStandaloneShelf()
    {
        await Page.GotoAsync(Links.Library());
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.ForsakenGods.ToString())).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync("/api/library", "GET", () => Library.Shelf.SearchBox.FillAsync("under"));

        response.Url.ShouldContain("q=under");
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.TidewaterChronicles.ToString())).ToBeVisibleAsync();
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.ForsakenGods.ToString())).ToHaveCountAsync(0);
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.AshenCrown.ToString())).ToHaveCountAsync(0);
        await Expect(Library.StackGroup.GetByTestId(LibraryTestIds.SeriesName)).ToHaveCountAsync(1);
        await Expect(Library.StandaloneGroup).ToHaveTextAsync(string.Empty);
    }

    [Test, Order(2)]
    public async Task Step2_WhenListGroupedBySeriesAndSortedByLastEdited_ShouldShowGroupRowsAndStandalone()
    {
        await Page.GotoAsync(Links.Library());
        await Expect(Library.Shelf.Shelf).ToBeVisibleAsync();

        await Library.ViewList.ClickAsync();
        await Expect(Library.Shelf.ListView).ToBeVisibleAsync();
        await Library.Shelf.GroupControl.SelectOptionAsync("series");
        // Last edited is the default sort: change it first so choosing it is a visible step.
        await Page.RunAndWaitForApiAsync("/api/library", "GET", () => Library.Shelf.SortControl.SelectOptionAsync("title"));
        var response = await Page.RunAndWaitForApiAsync("/api/library", "GET", () => Library.Shelf.SortControl.SelectOptionAsync("lastEdited"));

        response.Ok.ShouldBeTrue();
        await Expect(Library.Shelf.SortControl).ToHaveValueAsync("lastEdited");
        await Expect(Library.GroupRows).ToHaveCountAsync(4);
        await Expect(Library.GroupRows.Nth(0)).ToContainTextAsync("Forsaken Gods · 3");
        await Expect(Library.GroupRows.Nth(2)).ToContainTextAsync("Tidewater Chronicles · 2");
        await Expect(Library.GroupRows.Nth(3)).ToContainTextAsync("Standalone · 6");
        await Expect(Library.Shelf.ListRow(Undertow)).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Step3_WhenUndertowClickedInTheList_ShouldOpenItsPlan()
    {
        await Page.GotoAsync(Links.Library() + Links.Query(("view", "list"), ("sort", "lastEdited")));
        await Expect(Library.Shelf.ListRow(Undertow)).ToBeVisibleAsync();

        await Library.Shelf.ListRow(Undertow).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"{Regex.Escape(UndertowPlan)}$"));
    }

    [Test, Order(4)]
    public async Task Step4_WhenCommandPaletteOpenedWithTheHotkeyAndUndertowEntered_ShouldOpenItsPlan()
    {
        var palette = new CommandPalette(Page);
        // "From anywhere": start on another book's References screen, not the Library.
        await Page.GotoAsync(Links.References(SeedIds.Books.ProductEngineering.ToString()));
        await Expect(Page.GetByTestId(TestIds.Shell.BookBar)).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Control+k");
        await Expect(palette.Root).ToBeVisibleAsync();
        await palette.Input.FillAsync("undertow");
        await Expect(palette.Book(Undertow)).ToHaveAttributeAsync("aria-selected", "true");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{SeedIds.Books.Undertow}/(plan|write|references|layout|settings)$"));
        await Expect(palette.Root).ToBeHiddenAsync();
    }
}
