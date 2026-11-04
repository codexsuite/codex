using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J19 negative paths, each from the seed's state as the earlier ones leave it (none of them writes a manuscript export):
// a book with no chapters, cancelling the dialog, a chapter scope with no chapter picked, a failed job with Retry, and a
// second export of a kind that is already running.
[TestFixture, Category("J19")]
public class J19_ExportAManuscript_Negative : JourneyTest
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private static readonly LocatorAssertionsToContainTextOptions Built = new() { Timeout = 120_000 };

    private SettingsScreen Settings => new(Page);

    private async Task OpenDialogAsync(string book)
    {
        await Page.GotoAsync(Links.Settings(book, Links.Hash(("archive", null))));
        await Settings.ExportManuscript.ClickAsync();
        await Expect(Settings.ManuscriptDialog).ToBeVisibleAsync();
    }

    private async Task<long> JobsAsync(Guid book) =>
        await ScalarSqlAsync<long>("SELECT count(*) FROM export_job WHERE book_id = @book AND kind IN ('docx', 'markdown', 'epub')", ("book", book));

    [Test, Order(1)]
    public async Task Step1_WhenTheBookHasNoChapters_ShouldSayNothingToExportAndQueueNothing()
    {
        await OpenDialogAsync(SeedIds.Books.Northbound.ToString());

        await Expect(Settings.ManuscriptError).ToHaveTextAsync("Nothing to export. This book has no chapters.");
        await Expect(Settings.ManuscriptSubmit).ToBeDisabledAsync();
        (await JobsAsync(SeedIds.Books.Northbound)).ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task Step2_WhenTheDialogIsCancelled_ShouldQueueNothing()
    {
        await OpenDialogAsync(ProductEngineering);

        await Settings.ManuscriptCancel.ClickAsync();

        await Expect(Settings.ManuscriptDialog).ToBeHiddenAsync();
        (await JobsAsync(SeedIds.Books.ProductEngineering)).ShouldBe(0);
    }

    [Test, Order(3)]
    public async Task Step3_WhenScopeIsChapterWithNoChapterPicked_ShouldDisableExport()
    {
        await OpenDialogAsync(ProductEngineering);

        await Settings.ManuscriptScope.SelectOptionAsync("chapter");

        await Expect(Settings.ManuscriptSubmit).ToBeDisabledAsync();
        await Settings.ManuscriptChapter.SelectOptionAsync(SeedIds.Chapters.Ch3.ToString());
        await Expect(Settings.ManuscriptSubmit).ToBeEnabledAsync();
    }

    [Test, Order(4)]
    public async Task Step4_WhenAJobFailed_ShouldShowTheErrorAndRetryMakesANewRow()
    {
        await ScalarSqlAsync<Guid>(
            """
            INSERT INTO export_job (book_id, kind, settings, status, step, error, finished_at)
            VALUES (@book, 'markdown', '{"format":"markdown","scope":"book","includeBeats":false,"includeSummaries":false}', 'failed', 'Failed', 'The renderer fell over.', now())
            RETURNING id
            """,
            ("book", SeedIds.Books.ProductEngineering));
        await Page.GotoAsync(Links.Settings(ProductEngineering, Links.Hash(("archive", null))));
        await Expect(Settings.ManuscriptJobStatus).ToHaveTextAsync("Failed");
        await Expect(Settings.ManuscriptJobError).ToHaveTextAsync("The renderer fell over.");

        await Settings.ManuscriptRetry.ClickAsync();

        await Expect(Settings.ManuscriptDialog).ToBeVisibleAsync();
        await Expect(Settings.ManuscriptFormat("markdown")).ToHaveAttributeAsync("aria-checked", "true");
        await Settings.ManuscriptSubmit.ClickAsync();
        await Expect(Settings.ManuscriptJobs).ToHaveCountAsync(2);
        await Expect(Settings.ManuscriptJobStatus.First).ToContainTextAsync("Done", Built);
        (await JobsAsync(SeedIds.Books.ProductEngineering)).ShouldBe(2);
    }

    [Test, Order(5)]
    public async Task Step5_WhenAnExportOfTheKindIsRunning_ShouldDisableExportAndSayWhy()
    {
        await ScalarSqlAsync<Guid>(
            "INSERT INTO export_job (book_id, kind, settings, status, step, started_at) VALUES (@book, 'epub', '{}', 'running', 'Running', now()) RETURNING id",
            ("book", SeedIds.Books.ProductEngineering));
        await OpenDialogAsync(ProductEngineering);

        await Settings.ManuscriptFormat("epub").ClickAsync();

        await Expect(Settings.ManuscriptBusy).ToHaveTextAsync("An export is already running");
        await Expect(Settings.ManuscriptSubmit).ToBeDisabledAsync();
        await Settings.ManuscriptFormat("docx").ClickAsync();
        await Expect(Settings.ManuscriptSubmit).ToBeEnabledAsync();
    }
}
