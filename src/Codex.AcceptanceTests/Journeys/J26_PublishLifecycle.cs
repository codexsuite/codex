using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J26 · Publish lifecycle (#16 wave 5, L20.1): Undertow and Product Engineering go to Published with a snapshot, Product
// Engineering gets a second edition and a plain copy, and Undertow moves back to Revising. One story on one database.
[TestFixture, Category("J26")]
public class J26_PublishLifecycle : JourneyTest
{
    private static readonly string Undertow = SeedIds.Books.Undertow.ToString();
    private static readonly string Product = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Tidewater = SeedIds.Series.TidewaterChronicles.ToString();

    private static string _edition = "";
    private static string _copy = "";
    private static long _libraryBefore;

    private SettingsScreen Settings => new(Page);

    private SeriesScreen Series => new(Page);

    private Task<long> LiveBooksAsync() =>
        ScalarSqlAsync<long>("SELECT count(*) FROM book WHERE kind = 'book' AND archived_at IS NULL");

    private async Task<long> WordsOfAsync(string bookId) =>
        await ScalarSqlAsync<long>("SELECT coalesce(sum(word_count), 0) FROM scene_stats WHERE book_id = @id::uuid", ("id", bookId));

    [Test, Order(1)]
    public async Task Step1_OnTheSeries_PublishedStatShouldReadOne()
    {
        await Page.GotoAsync(Links.Series(Tidewater));

        await Expect(Series.StatPublished).ToContainTextAsync("1");
        _libraryBefore = await LiveBooksAsync();
    }

    [Test, Order(2)]
    public async Task Step2_WhenPublishedClicked_ShouldAskWithWordsAndRisks()
    {
        await Page.GotoAsync(Links.Settings(Undertow));

        await Settings.StatusOption("published").ClickAsync();

        await Expect(Settings.PublishDialog).ToBeVisibleAsync();
        var words = await WordsOfAsync(Undertow);
        await Expect(Settings.PublishMessage).ToContainTextAsync($"Words: {words:N0}.");
        await Expect(Settings.PublishWarning).ToHaveTextAsync("0 sources at risk.");
        await Expect(Settings.PublishMessage).ToContainTextAsync("A snapshot of the manuscript is saved.");
        (await ScalarSqlAsync<string>("SELECT status FROM book WHERE id = @id::uuid", ("id", Undertow))).ShouldBe("writing");
    }

    [Test, Order(3)]
    public async Task Step3_WhenConfirmed_ShouldPublishAndListASnapshotWithADownload()
    {
        await Page.GotoAsync(Links.Settings(Undertow));
        await Settings.StatusOption("published").ClickAsync();

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Undertow}/publish", "POST", () => Settings.PublishConfirm.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(Settings.PublishDialog).ToBeHiddenAsync();
        await Expect(Settings.StatusOption("published")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Settings.SnapshotsHeading).ToHaveTextAsync("Snapshots (1)");
        await Expect(Settings.SnapshotRows.First).ToContainTextAsync($"{await WordsOfAsync(Undertow):N0} words");
        await Expect(Settings.SnapshotRows.First).ToContainTextAsync("Published");
        // The export worker builds the zip; the list polls until the Download link appears.
        await Expect(Settings.SnapshotDownloads).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        (await ScalarSqlAsync<string>("SELECT status FROM book WHERE id = @id::uuid", ("id", Undertow))).ShouldBe("published");
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM export_job WHERE book_id = @id::uuid AND kind = 'source' AND status = 'succeeded' AND settings ->> 'snapshot' = 'true'",
            ("id", Undertow))).ShouldBe(1);
        // Edits stay possible: Published does not lock anything, and nothing has changed since the snapshot.
        await Page.GotoAsync(Links.Library());
        await Expect(new LibraryShelf(Page).Tile(Undertow).GetByTestId(LibraryTestIds.EditedSincePublication)).ToHaveCountAsync(0);
    }

    [Test, Order(4)]
    public async Task Step4_OnTheSeries_PublishedStatShouldReadTwo()
    {
        await Page.GotoAsync(Links.Series(Tidewater));

        await Expect(Series.StatPublished).ToContainTextAsync("2");
    }

    [Test, Order(5)]
    public async Task Step5_WhenProductEngineeringPublishedWithRisks_ShouldWarnAndNotBlock()
    {
        await Page.GotoAsync(Links.Settings(Product));

        await Settings.StatusOption("published").ClickAsync();

        await Expect(Settings.PublishWarning).ToHaveTextAsync(
            "3 sources are at risk. The book can be marked published, but Export for printer stays blocked.");
        await Settings.PublishConfirm.ClickAsync();
        await Expect(Settings.StatusOption("published")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Settings.SnapshotsHeading).ToHaveTextAsync("Snapshots (1)");
    }

    [Test, Order(6)]
    public async Task Step6_WhenAnEditionStarted_ShouldCopyTheBookAndItsSourcesWithoutSignOffs()
    {
        await Page.GotoAsync(Links.Settings(Product, Links.Hash(("archive", null))));
        await Settings.NewEdition.ClickAsync();
        await Expect(Settings.DuplicateTitle).ToHaveValueAsync("Product Engineering (2nd edition)");

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Product}/new-edition", "POST", () => Settings.DuplicateConfirm.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(Settings.DuplicateDone).ToContainTextAsync("Product Engineering (2nd edition)");
        _edition = (await ScalarSqlAsync<Guid>("SELECT id FROM book WHERE title = 'Product Engineering (2nd edition)'")).ToString();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM chapter WHERE book_id = @id::uuid", ("id", _edition))).ShouldBe(10);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene WHERE book_id = @id::uuid", ("id", _edition))).ShouldBe(15);
        (await WordsOfAsync(_edition)).ShouldBe(await WordsOfAsync(Product));
        (await ScalarSqlAsync<string>("SELECT status FROM book WHERE id = @id::uuid", ("id", _edition))).ShouldBe("planning");
        (await ScalarSqlAsync<Guid>("SELECT copied_from_id FROM book WHERE id = @id::uuid", ("id", _edition))).ShouldBe(SeedIds.Books.ProductEngineering);
        (await LiveBooksAsync()).ShouldBe(_libraryBefore + 1);

        await Page.GotoAsync(Links.References(_edition));

        var references = new ReferencesScreen(Page);
        await Expect(references.ClearedText).ToHaveTextAsync("0 of 10 cited sources cleared");
        await Expect(references.Rows).ToHaveCountAsync(12);
        (await ScalarSqlAsync<long>("SELECT risk_count::bigint FROM book_reference_summary WHERE book_id = @id::uuid", ("id", _edition))).ShouldBe(3);
    }

    [Test, Order(7)]
    public async Task Step7_WhenDuplicated_ShouldCopyTheBookWithoutSources()
    {
        await Page.GotoAsync(Links.Settings(Product, Links.Hash(("archive", null))));
        await Settings.Duplicate.ClickAsync();
        await Expect(Settings.DuplicateTitle).ToHaveValueAsync("Product Engineering (copy)");

        await Page.RunAndWaitForApiAsync($"/api/books/{Product}/duplicate", "POST", () => Settings.DuplicateConfirm.ClickAsync());

        await Expect(Settings.DuplicateDone).ToContainTextAsync("Product Engineering (copy)");
        _copy = (await ScalarSqlAsync<Guid>("SELECT id FROM book WHERE title = 'Product Engineering (copy)'")).ToString();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE book_id = @id::uuid", ("id", _copy))).ShouldBe(0);
        (await WordsOfAsync(_copy)).ShouldBe(await WordsOfAsync(Product));
        (await LiveBooksAsync()).ShouldBe(_libraryBefore + 2);
        await Page.GotoAsync(Links.References(_copy));
        await Expect(new ReferencesScreen(Page).Rows).ToHaveCountAsync(0);
    }

    [Test, Order(8)]
    public async Task Step8_WhenManuscriptChangedAfterThePublication_LibraryShouldSayEditedSincePublication()
    {
        // A save stamps book.last_edited_at; the test stamps it directly so the journey need not type into Write.
        await ScalarSqlAsync<object>("UPDATE book SET last_edited_at = now() + interval '1 minute' WHERE id = @id::uuid RETURNING 1", ("id", Product));

        await Page.GotoAsync(Links.Library());

        var shelf = new LibraryShelf(Page);
        await Expect(shelf.Tile(Product).GetByTestId(LibraryTestIds.EditedSincePublication)).ToHaveTextAsync("Edited since publication");
        await Expect(shelf.Tile(Undertow).GetByTestId(LibraryTestIds.EditedSincePublication)).ToHaveCountAsync(0);
    }

    [Test, Order(9)]
    public async Task Step9_WhenUndertowMovedBackToRevising_ShouldSaveWithoutAskingAndDropThePublishedStat()
    {
        await Page.GotoAsync(Links.Settings(Undertow));

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Undertow}/settings", "PATCH", () => Settings.StatusOption("revising").ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(Settings.PublishDialog).ToHaveCountAsync(0);
        (await ScalarSqlAsync<string>("SELECT status FROM book WHERE id = @id::uuid", ("id", Undertow))).ShouldBe("revising");
        await Page.GotoAsync(Links.Series(Tidewater));
        await Expect(Series.StatPublished).ToContainTextAsync("1");
        // The snapshot stays: it records what was published.
        (await ScalarSqlAsync<long>("SELECT count(*) FROM export_job WHERE book_id = @id::uuid AND settings ->> 'snapshot' = 'true'", ("id", Undertow))).ShouldBe(1);
    }
}
