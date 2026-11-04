using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Journeys;

// J17 · Delete, archive and restore a book or series (docs/journeys-wave5.md). A: archive Undertow and bring it back.
// B: archive Product Engineering (v1) and delete it for good. C: delete the Forsaken Gods series, its books go standalone
// with the series' Knowledge base copied onto each. One story on one database.
[TestFixture, Category("J17")]
public class J17_DeleteArchiveRestoreABook : JourneyTest
{
    private static readonly string Undertow = SeedIds.Books.Undertow.ToString();
    private static readonly string Tidewater = SeedIds.Series.TidewaterChronicles.ToString();
    private static readonly string V1 = SeedIds.Books.ProductEngineeringV1.ToString();
    private static readonly string ForsakenGods = SeedIds.Series.ForsakenGods.ToString();

    private SettingsScreen Settings => new(Page);

    private LibraryScreen Library => new(Page);

    private SeriesScreen Series => new(Page);

    private async Task ArchiveFromSettingsAsync(string bookId)
    {
        await Page.GotoAsync(Links.Settings(bookId, Links.Hash(("archive", null))));
        await Settings.Archive.ClickAsync();
        await Expect(Settings.ArchiveDialog).ToBeVisibleAsync();
        await Page.RunAndWaitForApiAsync($"/api/books/{bookId}/archive", "POST", () => Settings.ArchiveConfirm.ClickAsync());
        await Expect(Settings.ArchivedBar).ToBeVisibleAsync();
    }

    [Test, Order(1)]
    public async Task Step2_WhenUndertowArchived_ShouldShowToastAndArchivedBar()
    {
        await ArchiveFromSettingsAsync(Undertow);

        await Expect(Settings.Toast).ToContainTextAsync("Undertow archived.");
        await Expect(Settings.ArchivedBar).ToContainTextAsync("This book is archived: hidden from Library and the palette. Nothing was deleted.");
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM book WHERE id = @id", ("id", SeedIds.Books.Undertow))).ShouldNotBeNull();
        (await ScalarSqlAsync<int>("SELECT series_number FROM book WHERE id = @id", ("id", SeedIds.Books.Undertow))).ShouldBe(2);
    }

    [Test, Order(2)]
    public async Task Step3_OnLibrary_TidewaterShouldCountOneBookAndLeaveUndertowOutOfTheTotals()
    {
        await Page.GotoAsync(Links.Library());

        await Expect(LibraryScreen.StackCount(new LibraryShelf(Page).SeriesStack(Tidewater))).ToHaveTextAsync("1");
        await Expect(Library.Shelf.Tile(Undertow)).ToHaveCountAsync(0);

        await new LibraryShelf(Page).SeriesStack(Tidewater).ClickAsync();
        await Expect(Series.StatBooks).ToContainTextAsync("1");
        await Expect(Series.StatWords).ToContainTextAsync("1,035");

        var palette = new CommandPalette(Page);
        await Page.Keyboard.PressAsync("Control+k");
        await palette.Input.FillAsync("undertow");
        await Expect(palette.Empty).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
    }

    [Test, Order(3)]
    public async Task Step5_WhenRestoredFromTheArchivedList_ShouldReturnAsBook002()
    {
        await Page.GotoAsync(Links.Library() + Links.Query(("status", "archived")));
        await Expect(Library.Shelf.Tile(Undertow)).ToBeVisibleAsync();

        await Page.RunAndWaitForApiAsync($"/api/books/{Undertow}/restore", "POST", () => Library.Restore(Undertow).ClickAsync());

        await Expect(Library.Shelf.Tile(Undertow)).ToHaveCountAsync(0);
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM book WHERE id = @id", ("id", SeedIds.Books.Undertow))).ShouldBeNull();
        (await ScalarSqlAsync<int>("SELECT series_number FROM book WHERE id = @id", ("id", SeedIds.Books.Undertow))).ShouldBe(2);
        await Page.GotoAsync(Links.Library());
        await Expect(LibraryScreen.StackCount(new LibraryShelf(Page).SeriesStack(Tidewater))).ToHaveTextAsync("2");
        await new LibraryShelf(Page).SeriesStack(Tidewater).ClickAsync();
        await Expect(Series.StatWords).ToContainTextAsync("2,130");
    }

    [Test, Order(4)]
    public async Task Step7_WhenArchivedBookDeletePermanentlyChosen_ShouldNeedTheTypedTitle()
    {
        await ArchiveFromSettingsAsync(V1);
        await Page.GotoAsync(Links.Library() + Links.Query(("status", "archived")));

        await Library.BookMenu(V1).ClickAsync();
        await Library.DeleteBookItem(V1).ClickAsync();

        await Expect(Library.DeleteBookDialog).ToBeVisibleAsync();
        await Expect(Library.DeleteBookImpact).ToContainTextAsync("3 chapters, 6 scenes, 1,155 words, 0 sources");
        await Expect(Page.GetByTestId(LibraryTestIds.DeleteBookExport)).ToBeVisibleAsync();
        await Expect(Library.DeleteBookConfirm).ToBeDisabledAsync();
        await Library.DeleteBookTitle.FillAsync("Product Engineering");
        await Expect(Library.DeleteBookConfirm).ToBeDisabledAsync();
        await Library.DeleteBookTitle.FillAsync("Product Engineering (v1)");
        await Expect(Library.DeleteBookConfirm).ToBeEnabledAsync();
    }

    [Test, Order(5)]
    public async Task Step8_WhenDeleteConfirmed_ShouldRemoveTheBookAndItsRows()
    {
        await Page.GotoAsync(Links.Library() + Links.Query(("status", "archived")));
        await Library.BookMenu(V1).ClickAsync();
        await Library.DeleteBookItem(V1).ClickAsync();
        await Library.DeleteBookTitle.FillAsync("Product Engineering (v1)");

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{V1}", "DELETE", () => Library.DeleteBookConfirm.ClickAsync());

        response.Status.ShouldBe(204);
        await Expect(Library.DeleteBookDialog).ToBeHiddenAsync();
        await Expect(Library.Shelf.Tile(V1)).ToHaveCountAsync(0);
        // Deleted for good from the author's side; the rows stay, marked deleted (#85: no hard deletes).
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book WHERE id = @id AND deleted_at IS NOT NULL", ("id", SeedIds.Books.ProductEngineeringV1))).ShouldBe(1);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book WHERE id = @id", ("id", SeedIds.Books.ProductEngineering))).ShouldBe(1);
    }

    [Test, Order(6)]
    public async Task Step9_WhenSeriesDeleteChosen_ShouldSayWhatTheBooksKeep()
    {
        await Page.GotoAsync(Links.Series(ForsakenGods));

        await Series.SettingsButton.ClickAsync();
        await Series.DeleteButton.ClickAsync();

        await Expect(Series.DeleteDialog).ToBeVisibleAsync();
        await Expect(Series.DeleteImpact).ToContainTextAsync("Its 3 books become standalone");
        await Expect(Series.DeleteImpact).ToContainTextAsync("Spells, 3 entries");
        await Expect(Series.DeleteImpact).ToContainTextAsync("Playtested");
    }

    [Test, Order(7)]
    public async Task Step10_WhenSeriesDeleted_ShouldMakeTheBooksStandaloneWithTheirOwnSpells()
    {
        await Page.GotoAsync(Links.Series(ForsakenGods));
        await Series.SettingsButton.ClickAsync();
        await Series.DeleteButton.ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/series/{ForsakenGods}", "DELETE", () => Series.DeleteConfirm.ClickAsync());

        response.Status.ShouldBe(204);
        await Expect(Library.Shelf.Root).ToBeVisibleAsync();
        await Expect(Library.Shelf.SeriesStack(ForsakenGods)).ToHaveCountAsync(0);
        foreach (var book in new[] { SeedIds.Books.PlayersGuide, SeedIds.Books.GamemastersCodex, SeedIds.Books.BestiaryOfTheFallen })
        {
            await Expect(Library.StandaloneGroup.GetByTestId(TestIds.Shell.Tile(book.ToString()))).ToBeVisibleAsync();
            (await ScalarSqlAsync<long>(
                """
                SELECT count(*) FROM kb_entry e JOIN kb_category c ON c.id = e.category_id
                WHERE e.book_id = @id AND c.name = 'Spells' AND e.archived_at IS NULL
                """, ("id", book))).ShouldBe(3);
            (await ScalarSqlAsync<long>(
                "SELECT count(*) FROM label WHERE book_id = @id AND name = 'Playtested'", ("id", book))).ShouldBe(1);
            (await ScalarSqlAsync<Guid?>("SELECT series_id FROM book WHERE id = @id", ("id", book))).ShouldBeNull();
        }

        // Deleting a series is a soft delete (#73): the emptied series row is archived, not removed.
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NOT NULL FROM book WHERE id = @id", ("id", SeedIds.Series.ForsakenGods))).ShouldBeTrue();
    }
}
