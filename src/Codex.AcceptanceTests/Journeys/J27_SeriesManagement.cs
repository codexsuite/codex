using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.TestSupport;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Journeys;

// J27 · Series management (docs/journeys-wave5.md): make a series from two standalone books, reorder it, move a Knowledge
// base entry up to the series, edit a series entry from another book, take a book out and add one in.
[TestFixture, Category("J27")]
public class J27_SeriesManagement : JourneyTest
{
    private const string SeriesName = "Product Engineering Editions";

    private static readonly string Pe = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string V1 = SeedIds.Books.ProductEngineeringV1.ToString();
    private static readonly string Players = SeedIds.Books.PlayersGuide.ToString();
    private static readonly string Gamemasters = SeedIds.Books.GamemastersCodex.ToString();
    private static readonly string Saltwater = SeedIds.Books.SaltwaterLedger.ToString();
    private static string _series = "";

    private LibraryScreen Library => new(Page);

    private SeriesScreen Series => new(Page);

    private SettingsScreen Settings => new(Page);

    private KbSidebar Sidebar => new(Page);

    private KbEntryWindow Window => new(Page);

    private Task<int?> NumberAsync(Guid book) =>
        ScalarSqlAsync<int?>("SELECT series_number FROM book WHERE id = @id", ("id", book));

    [Test, Order(1)]
    public async Task Step1_WhenNewSeriesCreatedFromTwoBooks_ShouldNumberThemInPickedOrder()
    {
        await Page.GotoAsync(Links.Library());
        await Library.NewSeries.ClickAsync();
        await Expect(Library.NewSeriesDialog).ToBeVisibleAsync();
        await Library.NewSeriesName.FillAsync(SeriesName);
        await Library.NewSeriesBook(V1).CheckAsync();
        await Library.NewSeriesBook(Pe).CheckAsync();

        var created = await Page.RunAndWaitForApiAsync("/api/series", "POST", () => Library.NewSeriesCreate.ClickAsync());

        created.Status.ShouldBe(201);
        await Expect(Library.NewSeriesDialog).ToBeHiddenAsync();
        _series = (await ScalarSqlAsync<Guid>("SELECT id FROM book WHERE kind = 'series' AND title = @t", ("t", SeriesName))).ToString();
        (await NumberAsync(SeedIds.Books.ProductEngineeringV1)).ShouldBe(1);
        (await NumberAsync(SeedIds.Books.ProductEngineering)).ShouldBe(2);
        await Expect(Library.Shelf.Tile(V1)).ToHaveCountAsync(0);
        await Expect(LibraryScreen.StackCount(Library.Shelf.SeriesStack(_series))).ToHaveTextAsync("2");

        await Library.Shelf.SeriesStack(_series).ClickAsync();
        await Expect(Series.StatBooks).ToContainTextAsync("2");
        await Expect(Series.StatWords).ToContainTextAsync("14,858");
    }

    [Test, Order(2)]
    public async Task Step3_WhenProductEngineeringMovedUp_ShouldBecomeBook001()
    {
        await Page.GotoAsync(Links.Series(_series));
        await Series.SettingsButton.ClickAsync();

        await Page.GetByTestId(LibraryTestIds.SeriesMoveUp(Pe)).ClickAsync();
        await Page.RunAndWaitForApiAsync($"/api/series/{_series}/order", "PUT", () => Series.Save.ClickAsync());

        await Expect(Series.Dialog).ToBeHiddenAsync();
        (await NumberAsync(SeedIds.Books.ProductEngineering)).ShouldBe(1);
        (await NumberAsync(SeedIds.Books.ProductEngineeringV1)).ShouldBe(2);
    }

    [Test, Order(3)]
    public async Task Step4_WhenConwaysLawMovedToTheSeries_ShouldAppearInTheOtherBookToo()
    {
        await Page.GotoAsync(Links.Plan(Pe));
        await Sidebar.Item(SeedIds.KbEntries.ConwaysLaw).ClickAsync();
        await Expect(Window.Root).ToBeVisibleAsync();
        await Expect(Window.ScopeBadge).ToHaveCountAsync(0);

        await Window.Menu.ClickAsync();
        await Window.MenuPromote.ClickAsync();
        await Expect(Window.PromoteDialog).ToBeVisibleAsync();
        var moved = await Page.RunAndWaitForApiAsync(
            $"/api/kb/entries/{SeedIds.KbEntries.ConwaysLaw}/promote", "POST", () => Window.PromoteConfirm.ClickAsync());

        moved.Status.ShouldBe(200);
        await Expect(Window.ScopeBadge).ToHaveTextAsync("Series");
        (await ScalarSqlAsync<Guid>("SELECT book_id FROM kb_entry WHERE id = @id", ("id", SeedIds.KbEntries.ConwaysLaw))).ToString().ShouldBe(_series);
        await Page.GotoAsync(Links.Plan(V1));
        await Expect(Sidebar.Item(SeedIds.KbEntries.ConwaysLaw)).ToContainTextAsync("Conway's Law");
        await Page.GotoAsync(Links.Plan(Pe));
        await Expect(Sidebar.Item(SeedIds.KbEntries.ConwaysLaw)).ToBeVisibleAsync();
    }

    [Test, Order(4)]
    public async Task Step5_WhenSeriesEntryEditedFromOneBook_ShouldShowTheChangeInAnother()
    {
        await Page.GotoAsync(Links.Plan(Players));
        await Sidebar.Item(SeedIds.KbEntries.Gravetouch).ClickAsync();
        await Expect(Window.ScopeBadge).ToHaveTextAsync("Series");
        await Page.RunAndWaitForApiAsync($"/api/kb/entries/{SeedIds.KbEntries.Gravetouch}", "PATCH", async () =>
        {
            await Window.Summary.FillAsync("Cold to the touch, and then not.");
            await Window.Name.FocusAsync();
        });

        await Page.GotoAsync(Links.Plan(Gamemasters));
        await Sidebar.Item(SeedIds.KbEntries.Gravetouch).ClickAsync();
        await Expect(Window.Summary).ToHaveValueAsync("Cold to the touch, and then not.");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_entry WHERE name = 'Gravetouch'")).ShouldBe(1);
    }

    [Test, Order(5)]
    public async Task Step6_WhenGamemastersCodexDetached_ShouldKeepItsOwnSpellsAndCloseTheNumberGap()
    {
        await Page.GotoAsync(Links.Settings(Gamemasters, Links.Hash(("series", null))));
        await Settings.SeriesSelect.SelectOptionAsync(new SelectOptionValue { Label = "None (standalone)" });
        await Settings.MoveReview.ClickAsync();
        await Expect(Settings.MoveDialog).ToBeVisibleAsync();

        var moved = await Page.RunAndWaitForApiAsync($"/api/books/{Gamemasters}/series-move", "POST", () => Settings.MoveConfirm.ClickAsync());

        moved.Status.ShouldBe(200);
        (await ScalarSqlAsync<Guid?>("SELECT series_id FROM book WHERE id = @id", ("id", SeedIds.Books.GamemastersCodex))).ShouldBeNull();
        (await ScalarSqlAsync<long>(
            """
            SELECT count(*) FROM kb_entry e JOIN kb_category c ON c.id = e.category_id
            WHERE e.book_id = @id AND c.name = 'Spells' AND e.archived_at IS NULL
            """, ("id", SeedIds.Books.GamemastersCodex))).ShouldBe(3);
        // The move closes the gap (SeriesMoveCommands, "renumber" step), so Bestiary moves from Book 003 to 002.
        (await NumberAsync(SeedIds.Books.BestiaryOfTheFallen)).ShouldBe(2);
        await Page.GotoAsync(Links.Series(SeedIds.Series.ForsakenGods.ToString()));
        await Expect(Series.StatBooks).ToContainTextAsync("2");
    }

    [Test, Order(6)]
    public async Task Step7_WhenSaltwaterLedgerAddedAsBook003_ShouldJoinTheSeries()
    {
        await Page.GotoAsync(Links.Settings(Saltwater, Links.Hash(("series", null))));
        await Settings.SeriesSelect.SelectOptionAsync(new SelectOptionValue { Label = SeriesName });
        await Settings.MoveReview.ClickAsync();
        await Expect(Settings.MoveDialog).ToBeVisibleAsync();

        var moved = await Page.RunAndWaitForApiAsync($"/api/books/{Saltwater}/series-move", "POST", () => Settings.MoveConfirm.ClickAsync());

        moved.Status.ShouldBe(200);
        (await ScalarSqlAsync<Guid?>("SELECT series_id FROM book WHERE id = @id", ("id", SeedIds.Books.SaltwaterLedger)))?.ToString().ShouldBe(_series);
        (await NumberAsync(SeedIds.Books.SaltwaterLedger)).ShouldBe(3);
        await Expect(Settings.NumberSelect).ToHaveValueAsync("3");
        await Page.GotoAsync(Links.Series(_series));
        await Expect(Series.StatBooks).ToContainTextAsync("3");
    }
}
