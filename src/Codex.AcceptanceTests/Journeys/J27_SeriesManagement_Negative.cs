using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Journeys;

// J27 negative paths, from the seed: nothing is written until the author chooses, and an empty or same-named series is
// allowed. Not built (reported): the duplicate-number error (the number picker cannot pick a taken number) and the
// published-order warning.
[TestFixture, Category("J27")]
public class J27_SeriesManagement_Negative : JourneyTest
{
    private static readonly string Pe = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Saltwater = SeedIds.Books.SaltwaterLedger.ToString();

    private LibraryScreen Library => new(Page);

    private SettingsScreen Settings => new(Page);

    private KbSidebar Sidebar => new(Page);

    private KbEntryWindow Window => new(Page);

    private Task<long> SeriesCountAsync(string? title = null) =>
        ScalarSqlAsync<long>("SELECT count(*) FROM book WHERE kind = 'series' AND (@t::text IS NULL OR title = @t)", ("t", (object?)title ?? DBNull.Value));

    [Test, Order(1)]
    public async Task NewSeries_WhenNameEmpty_ShouldShowTheErrorAndWriteNothing()
    {
        var before = await SeriesCountAsync();
        await Page.GotoAsync(Links.Library());
        await Library.NewSeries.ClickAsync();

        await Library.NewSeriesCreate.ClickAsync();

        await Expect(Library.NewSeriesNameError).ToBeVisibleAsync();
        await Library.NewSeriesCancel.ClickAsync();
        await Expect(Library.NewSeriesDialog).ToBeHiddenAsync();
        (await SeriesCountAsync()).ShouldBe(before);
    }

    [Test, Order(2)]
    public async Task NewSeries_WhenNoBooksAndAnExistingName_ShouldBeAllowedWithANotice()
    {
        await Page.GotoAsync(Links.Library());
        await Library.NewSeries.ClickAsync();
        await Library.NewSeriesName.FillAsync("Tidewater Chronicles");
        await Expect(Library.NewSeriesNotice).ToBeVisibleAsync();

        var created = await Page.RunAndWaitForApiAsync("/api/series", "POST", () => Library.NewSeriesCreate.ClickAsync());

        created.Status.ShouldBe(201);
        (await SeriesCountAsync("Tidewater Chronicles")).ShouldBe(2);
    }

    [Test, Order(3)]
    public async Task Promote_WhenTheSeriesHasTheName_ShouldOfferMergeAndWriteNothingUntilChosen()
    {
        var series = await ScalarSqlAsync<Guid>(
            "INSERT INTO book (kind, owner_id, title) SELECT 'series', owner_id, 'Editions' FROM book WHERE id = @pe RETURNING id",
            ("pe", SeedIds.Books.ProductEngineering));
        await ScalarSqlAsync<Guid>("UPDATE book SET series_id = @s, series_number = 1 WHERE id = @pe RETURNING id", ("s", series), ("pe", SeedIds.Books.ProductEngineering));
        var category = await ScalarSqlAsync<Guid>("INSERT INTO kb_category (book_id, name, position) VALUES (@s, 'Laws', 'a0') RETURNING id", ("s", series));
        await ScalarSqlAsync<Guid>(
            "INSERT INTO kb_entry (book_id, category_id, name, position) VALUES (@s, @c, 'Conway''s Law', 'a0') RETURNING id", ("s", series), ("c", category));
        await Page.GotoAsync(Links.Plan(Pe));
        await Sidebar.Item(SeedIds.KbEntries.ConwaysLaw).ClickAsync();
        await Window.Menu.ClickAsync();
        await Window.MenuPromote.ClickAsync();

        await Window.PromoteConfirm.ClickAsync();

        await Expect(Window.PromoteMerge).ToBeVisibleAsync();
        await Expect(Window.PromoteDialog).ToContainTextAsync("already has an entry named Conway's Law");
        await Window.PromoteCancel.ClickAsync();
        await Expect(Window.PromoteDialog).ToBeHiddenAsync();
        (await ScalarSqlAsync<Guid>("SELECT book_id FROM kb_entry WHERE id = @id", ("id", SeedIds.KbEntries.ConwaysLaw))).ShouldBe(SeedIds.Books.ProductEngineering);
    }

    [Test, Order(4)]
    public async Task Move_WhenReviewCancelled_ShouldLeaveTheBookStandalone()
    {
        await Page.GotoAsync(Links.Settings(Saltwater, Links.Hash(("series", null))));
        await Settings.SeriesSelect.SelectOptionAsync(new SelectOptionValue { Label = "Tidewater Chronicles" });
        await Settings.MoveReview.ClickAsync();
        await Expect(Settings.MoveDialog).ToBeVisibleAsync();

        await Settings.MoveCancel.ClickAsync();

        await Expect(Settings.MoveDialog).ToBeHiddenAsync();
        (await ScalarSqlAsync<Guid?>("SELECT series_id FROM book WHERE id = @id", ("id", SeedIds.Books.SaltwaterLedger))).ShouldBeNull();
    }
}
