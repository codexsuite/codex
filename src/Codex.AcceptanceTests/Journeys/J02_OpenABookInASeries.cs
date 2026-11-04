using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J2 · Open a book in a series (#16 Part 2): Library -> The Ashen Crown stack -> Series -> Hollow Throne -> Plan -> back.
[TestFixture, Category("J2")]
public class J02_OpenABookInASeries : JourneyTest
{
    private static readonly string SeriesId = SeedIds.Series.AshenCrown.ToString();
    private static readonly string BookId = SeedIds.Books.HollowThrone.ToString();

    private SeriesScreen Series => new(Page);

    [Test, Order(1)]
    public async Task Step1_OnLibrary_AshenCrownStackShouldShowThreeCoversAndBadge4()
    {
        await Page.GotoAsync(Links.Library());

        var stack = new LibraryShelf(Page).SeriesStack(SeriesId);
        await Expect(stack).ToBeVisibleAsync();
        await Expect(LibraryScreen.Covers(stack)).ToHaveCountAsync(3);
        await Expect(LibraryScreen.StackCount(stack)).ToHaveTextAsync("4");
        await Expect(Page.GetByTestId(LibraryTestIds.StackGroup)).ToContainTextAsync("The Ashen Crown");
    }

    [Test, Order(2)]
    public async Task Step2_WhenStackClicked_ShouldOpenSeriesScreenWithTotals()
    {
        await Page.GotoAsync(Links.Library());

        await new LibraryShelf(Page).SeriesStack(SeriesId).ClickAsync();

        await Expect(Series.Root).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"#series={SeriesId}$"));
        await Expect(Series.Title).ToHaveTextAsync("The Ashen Crown");
        // The mock's "/" is drawn as a chevron.
        await Expect(Series.Breadcrumb).ToHaveTextAsync(new Regex(@"^Library\s*[/›]\s*The Ashen Crown$"));
        await Expect(Series.StatBooks).ToContainTextAsync("4");
        // Seed as built (D3): 3,540 words, not Part 1's 338,720.
        await Expect(Series.StatWords).ToContainTextAsync("3,540");
        await Expect(Series.StatPublished).ToContainTextAsync("2");
        var edited = await ScalarSqlAsync<DateTime>(
            "SELECT last_edited_at FROM book_stats WHERE book_id = @id", ("id", SeedIds.Series.AshenCrown));
        await Expect(Series.StatLastEdit).ToContainTextAsync(ExpectedLabels.Relative(new DateTimeOffset(edited)));
    }

    [Test, Order(3)]
    public async Task Step3_WhenHollowThroneClicked_ShouldOpenItsPlan()
    {
        await Page.GotoAsync(Links.Series(SeriesId));
        var tile = Series.Tile(BookId);
        await Expect(tile).ToContainTextAsync("Hollow Throne");
        await Expect(tile).ToContainTextAsync("Book 003");
        // The status is stored lower case and capitalised by CSS.
        await Expect(tile).ToContainTextAsync(new Regex("revising", RegexOptions.IgnoreCase));

        await tile.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/plan$"));
    }

    [Test, Order(4)]
    public async Task Step4_OnPlan_SidebarShouldShowSeriesCharactersAndPlacesOnly()
    {
        var sidebar = new KbSidebar(Page);
        await Page.GotoAsync(Links.Plan(BookId));

        var series = sidebar.Section("series");
        await Expect(series).ToContainTextAsync("From The Ashen Crown");
        await Expect(series.GetByTestId(KbTestIds.Category(SeedIds.KbCategories.AshenCrownCharacters))).ToContainTextAsync("Characters");
        await Expect(series.GetByTestId(KbTestIds.Category(SeedIds.KbCategories.AshenCrownPlaces))).ToContainTextAsync("Places");
        // No book-level entries: every category on screen is one of the series' two.
        await Expect(sidebar.Section("book")).ToHaveCountAsync(0);
        await Expect(sidebar.Category(SeedIds.KbCategories.Laws)).ToHaveCountAsync(0);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM book_scope WHERE book_id = @id", ("id", SeedIds.Books.HollowThrone))).ShouldBeGreaterThan(0);
    }

    [Test, Order(5)]
    public async Task Step5_WhenBackClicked_ShouldReturnToTheSeriesScreen()
    {
        var bar = new BookBar(Page);
        await Page.GotoAsync(Links.Series(SeriesId));
        await Series.Tile(BookId).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/plan$"));
        await Expect(bar.BackLink).ToHaveAttributeAsync("href", Links.Series(SeriesId));

        await bar.BackLink.ClickAsync();

        await Expect(Series.Root).ToBeVisibleAsync();
        await Expect(Series.Title).ToHaveTextAsync("The Ashen Crown");
        await Expect(Page).ToHaveURLAsync(new Regex($"#series={SeriesId}$"));
    }
}
