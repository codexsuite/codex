using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;

namespace Codex.AcceptanceTests.Lanes.References;

// Import a BibTeX file holding one seeded source (beck1999) and one new one, then a cut-off RIS file.
[TestFixture]
public class ImportSourcesTests : ReferencesLaneTest
{
    private static FilePayload Payload(string name, string text) => new() { Name = name, MimeType = "text/plain", Buffer = System.Text.Encoding.UTF8.GetBytes(text) };

    [Test, Order(1)]
    public async Task Import_WhenTheFileRepeatsASeededSource_ShouldSkipItAndAddTheNewOne()
    {
        await Page.GotoAsync(Links.References(Book));
        var form = new AddSourceForm(Page);
        var screen = new ReferencesScreen(Page);
        await Expect(screen.Rows.First).ToBeVisibleAsync();
        var before = await screen.Rows.CountAsync();

        await form.ImportOpen.ClickAsync();
        await form.ImportFile.SetInputFilesAsync(Payload("library.bib",
            "@book{beck1999, author={Beck, Kent}, title={Extreme Programming Explained}, year={1999}}\n" +
            "@book{ries2011, author={Ries, Eric}, title={The Lean Startup}, publisher={Crown Business}, year={2011}}"));
        await Expect(form.ImportSummary).ToHaveTextAsync("1 new · 1 already in References");
        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Book}/references/import", "POST", () => form.ImportSubmit.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(form.ImportResult).ToHaveTextAsync("1 added");
        await Expect(screen.Rows).ToHaveCountAsync(before + 1);
    }

    [Test, Order(2)]
    public async Task Import_WhenTheSameFileIsImportedAgain_ShouldSkipEverythingAndOfferNothingToImport()
    {
        await Page.GotoAsync(Links.References(Book));
        var form = new AddSourceForm(Page);

        await form.ImportOpen.ClickAsync();
        await form.ImportFile.SetInputFilesAsync(Payload("library.bib",
            "@book{ries2011, author={Ries, Eric}, title={The Lean Startup}, year={2011}}"));

        await Expect(form.ImportSummary).ToHaveTextAsync("0 new · 1 already in References");
        await Expect(form.ImportSkipped).ToContainTextAsync("The Lean Startup: already in References");
        await Expect(form.ImportSubmit).ToBeDisabledAsync();
    }

    [Test, Order(3)]
    public async Task Import_WhenARisRecordHasNoEnd_ShouldReportItAndImportTheRest()
    {
        await Page.GotoAsync(Links.References(Book));
        var form = new AddSourceForm(Page);

        await form.ImportOpen.ClickAsync();
        await form.ImportFile.SetInputFilesAsync(Payload("export.ris", "TY  - BOOK\nTI  - Complete record\nER  - \nTY  - BOOK\nTI  - Cut off record"));

        await Expect(form.ImportSummary).ToHaveTextAsync("1 new");
        await Expect(form.ImportProblems).ToHaveTextAsync("Line 4: a record has no ER line, left out.");
        await Page.RunAndWaitForApiAsync($"/api/books/{Book}/references/import", "POST", () => form.ImportSubmit.ClickAsync());
        await Expect(form.ImportResult).ToHaveTextAsync("1 added");
    }
}
