using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Journeys;

// J17 negative paths, each from the seed: nothing is written when the author cancels, types the wrong title, or asks
// for something the rules refuse.
[TestFixture, Category("J17")]
public class J17_DeleteArchiveRestoreABook_Negative : JourneyTest
{
    private static readonly string Undertow = SeedIds.Books.Undertow.ToString();
    private static readonly string V1 = SeedIds.Books.ProductEngineeringV1.ToString();
    private static readonly string ForsakenGods = SeedIds.Series.ForsakenGods.ToString();

    private SettingsScreen Settings => new(Page);

    private LibraryScreen Library => new(Page);

    private SeriesScreen Series => new(Page);

    [Test, Order(1)]
    public async Task Archive_WhenCancelled_ShouldLeaveTheBookShelved()
    {
        await Page.GotoAsync(Links.Settings(Undertow, Links.Hash(("archive", null))));
        await Settings.Archive.ClickAsync();
        await Expect(Settings.ArchiveDialog).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Settings.ArchiveDialog).ToBeHiddenAsync();
        await Expect(Settings.Archive).ToBeFocusedAsync();
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM book WHERE id = @id", ("id", SeedIds.Books.Undertow))).ShouldBeNull();
    }

    [Test, Order(2)]
    public async Task Archive_WhenAPrintExportIsRunning_ShouldBeBlockedWithAMessage()
    {
        await ScalarSqlAsync<Guid>(
            """
            INSERT INTO export_job (book_id, kind, template_version_id, trim_size, status)
            SELECT @id, 'print', v.id, '6x9', 'running' FROM layout_template_version v LIMIT 1 RETURNING id
            """, ("id", SeedIds.Books.Undertow));
        await Page.GotoAsync(Links.Settings(Undertow, Links.Hash(("archive", null))));
        await Settings.Archive.ClickAsync();

        await Settings.ArchiveConfirm.ClickAsync();

        await Expect(Settings.LifecycleError).ToContainTextAsync("An export is running for this book");
        (await ScalarSqlAsync<DateTime?>("SELECT archived_at FROM book WHERE id = @id", ("id", SeedIds.Books.Undertow))).ShouldBeNull();
    }

    [Test, Order(3)]
    public async Task Delete_WhenBookIsNotArchived_ShouldNotBeOfferedAndTheApiShouldAnswer409()
    {
        await Page.GotoAsync(Links.Library());
        await Expect(Library.Shelf.Tile(V1)).ToBeVisibleAsync();
        await Expect(Library.BookMenu(V1)).ToHaveCountAsync(0);

        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var response = await client.DeleteAsync($"/api/books/{V1}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book WHERE id = @id", ("id", SeedIds.Books.ProductEngineeringV1))).ShouldBe(1);
    }

    [Test, Order(4)]
    public async Task Delete_WhenAnotherTitleTyped_ShouldKeepDeleteDisabledAndRemoveNothing()
    {
        await ScalarSqlAsync<Guid>("UPDATE book SET archived_at = now() WHERE id = @id RETURNING id", ("id", SeedIds.Books.ProductEngineeringV1));
        await Page.GotoAsync(Links.Library() + Links.Query(("status", "archived")));
        await Library.BookMenu(V1).ClickAsync();
        await Library.DeleteBookItem(V1).ClickAsync();

        await Library.DeleteBookTitle.FillAsync("Product Engineering");
        await Expect(Library.DeleteBookConfirm).ToBeDisabledAsync();
        await Page.Keyboard.PressAsync("Escape");

        await Expect(Library.DeleteBookDialog).ToBeHiddenAsync();
        await Expect(Library.BookMenu(V1)).ToBeFocusedAsync();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineeringV1))).ShouldBe(6);
    }

    [Test, Order(5)]
    public async Task DeleteSeries_WhenCancelled_ShouldKeepTheBooksInTheSeries()
    {
        await Page.GotoAsync(Links.Series(ForsakenGods));
        await Series.SettingsButton.ClickAsync();
        await Series.DeleteButton.ClickAsync();
        await Expect(Series.DeleteDialog).ToBeVisibleAsync();

        await Series.DeleteCancel.ClickAsync();

        await Expect(Series.DeleteDialog).ToBeHiddenAsync();
        await Expect(Series.Title).ToHaveTextAsync("Forsaken Gods");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book WHERE series_id = @id", ("id", SeedIds.Series.ForsakenGods))).ShouldBe(3);
    }
}
