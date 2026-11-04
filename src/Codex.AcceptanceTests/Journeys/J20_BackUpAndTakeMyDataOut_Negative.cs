using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Import;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;
using Codex.TestSupport.Fixtures;

namespace Codex.AcceptanceTests.Journeys;

// J20 negative paths, from the seed: a zip that is not a backup, a backup from a newer Codex, cancelling the dialog (none of
// them writes a book) and Export source while an export of the book runs.
[TestFixture, Category("J20")]
public class J20_BackUpAndTakeMyDataOut_Negative : JourneyTest
{
    private ImportScreen Import => new(Page);

    private async Task<long> BooksAsync() => await ScalarSqlAsync<long>("SELECT count(*) FROM book");

    private async Task PickAsync(byte[] zip)
    {
        await Page.GotoAsync(Links.Library());
        await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST",
            () => Import.PickFileAsync("codex_zip", "backup.zip", "application/zip", zip));
    }

    [Test, Order(1)]
    public async Task Step1_WhenTheZipIsNotABackup_ShouldSayAndDisableImport()
    {
        var books = await BooksAsync();

        await PickAsync(TestZip.Of(("notes/one.md", "# One")));

        await Expect(Page.GetByTestId(ImportTestIds.PreviewError)).ToContainTextAsync("This is not a Codex backup");
        await Expect(Import.Run).ToBeDisabledAsync();
        (await BooksAsync()).ShouldBe(books);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book_import WHERE status = 'importing'")).ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task Step2_WhenTheBackupIsFromANewerCodex_ShouldSayAndDisableImport()
    {
        var books = await BooksAsync();

        await PickAsync(TestZip.Of(("book.json", """{"formatVersion":99,"title":"From the future"}""")));

        await Expect(Page.GetByTestId(ImportTestIds.PreviewError)).ToContainTextAsync("This backup was made by a newer Codex");
        await Expect(Import.Run).ToBeDisabledAsync();
        (await BooksAsync()).ShouldBe(books);
    }

    [Test, Order(3)]
    public async Task Step3_WhenTheDialogIsCancelledOnTheMapping_ShouldCreateNoBook()
    {
        var books = await BooksAsync();
        await PickAsync(TestZip.Of(("book.json", """{"formatVersion":1,"title":"Tiny"}""")));
        await Expect(Import.Preview).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");

        await Expect(Import.Dialog).ToBeHiddenAsync();
        (await BooksAsync()).ShouldBe(books);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book_import WHERE source = 'codex_zip'")).ShouldBe(0);
    }

    [Test, Order(4)]
    public async Task Step4_WhenAnExportOfTheBookIsRunning_ShouldDisableExportSource()
    {
        await ScalarSqlAsync<Guid>(
            "INSERT INTO export_job (book_id, kind, status, step, started_at) VALUES (@book, 'source', 'running', 'Running', now()) RETURNING id",
            ("book", SeedIds.Books.ProductEngineering));

        await Page.GotoAsync(Links.Settings(SeedIds.Books.ProductEngineering.ToString(), Links.Hash(("archive", null))));

        await Expect(new SettingsScreen(Page).ExportSource).ToBeDisabledAsync();
        await Expect(new SettingsScreen(Page).ExportStatus).ToHaveTextAsync("Export running.");
    }
}
