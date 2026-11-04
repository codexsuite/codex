using System.IO.Compression;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J19 · Export a manuscript, not for print (docs/journeys-wave5.md): Product Engineering → Book settings → Archive & export →
// Export manuscript… as Word, then one chapter as Markdown, then EPUB with beats and summaries on, then Players Guide as
// Markdown (what the file left out is listed), then the whole book as Markdown (the Library's word count). Word and EPUB
// are converted by pandoc inside the print sandbox, so those two steps need pandoc and codex-sandbox as CI installs them.
// The negative paths are J19_ExportAManuscript_Negative.
[TestFixture, Category("J19")]
public class J19_ExportAManuscript : JourneyTest
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string PlayersGuide = SeedIds.Books.PlayersGuide.ToString();
    private static readonly LocatorAssertionsToContainTextOptions Built = new() { Timeout = 120_000 };

    private SettingsScreen Settings => new(Page);

    private async Task OpenArchiveAsync(string book)
    {
        await Page.GotoAsync(Links.Settings(book, Links.Hash(("archive", null))));
        await Expect(Settings.ExportManuscript).ToBeVisibleAsync();
    }

    // Exports with the dialog as it stands, waits for the newest job to finish and returns the file.
    private async Task<byte[]> SubmitAndDownloadAsync()
    {
        await Settings.ManuscriptSubmit.ClickAsync();
        await Expect(Settings.ManuscriptDialog).ToBeHiddenAsync();
        var job = Settings.ManuscriptJobs.First;
        await Expect(job.GetByTestId(SettingsTestIds.ManuscriptJobStatus)).ToContainTextAsync("Done", Built);
        var href = await job.GetByTestId(SettingsTestIds.ManuscriptDownload).GetAttributeAsync("href");
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return await api.GetByteArrayAsync(href!);
    }

    private static async Task<string> ReadAsync(ZipArchiveEntry entry)
    {
        await using var stream = await entry.OpenAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    // The scene text only: no title block, headings, footnote markers or definitions, bibliography.
    private static string Prose(byte[] file)
    {
        var text = System.Text.Encoding.UTF8.GetString(file);
        var body = text[(text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + 5)..];
        var end = Regex.Match(body, @"^(# Bibliography|\[\^\d+\]:)", RegexOptions.Multiline);
        body = end.Success ? body[..end.Index] : body;
        body = Regex.Replace(body, @"^#{1,6} .*$", string.Empty, RegexOptions.Multiline);
        return Regex.Replace(body, @"\[\^\d+\]", string.Empty);
    }

    [Test, Order(1)]
    public async Task Step1_WhenExportManuscriptOpened_ShouldOfferTheFormatsAndSayHowManySourcesAreAtRisk()
    {
        await OpenArchiveAsync(ProductEngineering);

        await Settings.ExportManuscript.ClickAsync();

        await Expect(Settings.ManuscriptDialog).ToBeVisibleAsync();
        await Expect(Settings.ManuscriptFormat("docx")).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(Settings.ManuscriptScope).ToHaveValueAsync("book");
        await Expect(Settings.ManuscriptBeats).Not.ToBeCheckedAsync();
        await Expect(Settings.ManuscriptSummaries).Not.ToBeCheckedAsync();
        await Expect(Settings.ManuscriptRisk).ToHaveTextAsync("3 sources are at risk. This export is not gated.");
        await Expect(Settings.ManuscriptSubmit).ToBeEnabledAsync();
    }

    [Test, Order(2)]
    public async Task Step2_WhenWordExported_ShouldBuildADocxWithChapterAndActHeadingsAndFootnotes()
    {
        await OpenArchiveAsync(ProductEngineering);
        await Settings.ExportManuscript.ClickAsync();

        var file = await SubmitAndDownloadAsync();

        await Expect(Settings.ManuscriptJobs.First).ToContainTextAsync("product-engineering.docx");
        (await ScalarSqlAsync<string>("SELECT status FROM export_job WHERE kind = 'docx'")).ShouldBe("succeeded");
        (await ScalarSqlAsync<bool>("SELECT output_asset_id IS NOT NULL FROM export_job WHERE kind = 'docx'")).ShouldBeTrue();
        using var zip = new ZipArchive(new MemoryStream(file));
        var document = await ReadAsync(zip.GetEntry("word/document.xml")!);
        Regex.Matches(document, "w:pStyle w:val=\"Heading2\"").Count.ShouldBe(10);
        Regex.Matches(document, "w:pStyle w:val=\"Heading1\"").Count.ShouldBe(4, "3 acts and the bibliography");
        document.ShouldNotContain("ref:");
        document.ShouldNotContain("::");
        var uses = await ScalarSqlAsync<long>("SELECT count(*) FROM reference_use WHERE archived_at IS NULL AND generation_id IS NULL AND book_id = @book", ("book", SeedIds.Books.ProductEngineering));
        Regex.Matches(await ReadAsync(zip.GetEntry("word/footnotes.xml")!), "<w:footnote (?![^>]*w:type=)").Count.ShouldBe((int)uses);
    }

    [Test, Order(3)]
    public async Task Step3_WhenChapter6ExportedAsMarkdown_ShouldHoldOnlyThatChapter()
    {
        await OpenArchiveAsync(ProductEngineering);
        await Settings.ExportManuscript.ClickAsync();
        await Settings.ManuscriptFormat("markdown").ClickAsync();
        await Settings.ManuscriptScope.SelectOptionAsync("chapter");
        await Expect(Settings.ManuscriptSubmit).ToBeDisabledAsync();
        await Settings.ManuscriptChapter.SelectOptionAsync(SeedIds.Chapters.Ch6.ToString());
        await Expect(Settings.ManuscriptSubmit).ToBeEnabledAsync();

        var file = await SubmitAndDownloadAsync();

        var text = System.Text.Encoding.UTF8.GetString(file);
        Regex.Matches(text, @"^## ", RegexOptions.Multiline).Count.ShouldBe(1);
        Regex.Matches(text, @"^# (?!Bibliography)", RegexOptions.Multiline).Count.ShouldBe(0);
        (await ScalarSqlAsync<int>("SELECT markdown_word_count(@text)", ("text", Prose(file)))).ShouldBe(640);
    }

    [Test, Order(4)]
    public async Task Step4_WhenEpubExportedWithBeatsAndSummaries_ShouldBeAPackageWithABlockQuotePerBeat()
    {
        await OpenArchiveAsync(ProductEngineering);
        await Settings.ExportManuscript.ClickAsync();
        await Settings.ManuscriptFormat("epub").ClickAsync();
        await Settings.ManuscriptBeats.CheckAsync();
        await Settings.ManuscriptSummaries.CheckAsync();

        var file = await SubmitAndDownloadAsync();

        using var zip = new ZipArchive(new MemoryStream(file));
        zip.Entries[0].FullName.ShouldBe("mimetype");
        zip.Entries.ShouldContain(e => e.FullName == "EPUB/nav.xhtml");
        var pages = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^EPUB/text/ch\d+\.xhtml$")).ToList();
        pages.Count.ShouldBe(14, "10 chapters, 3 act pages and the bibliography");
        var html = string.Concat(await Task.WhenAll(pages.Select(ReadAsync)));
        var beats = await ScalarSqlAsync<long>(
            "SELECT count(*) FROM beat b JOIN scene s ON s.id = b.scene_id WHERE s.book_id = @book AND b.archived_at IS NULL AND btrim(b.body) <> ''",
            ("book", SeedIds.Books.ProductEngineering));
        Regex.Matches(html, @"<blockquote>\s*<p>Beat:").Count.ShouldBe((int)beats);
    }

    [Test, Order(5)]
    public async Task Step5_WhenPlayersGuideExportedAsMarkdown_ShouldListWhatTheFileLeftOut()
    {
        await OpenArchiveAsync(PlayersGuide);
        await Settings.ExportManuscript.ClickAsync();
        await Settings.ManuscriptFormat("markdown").ClickAsync();

        await SubmitAndDownloadAsync();

        await Expect(Settings.ManuscriptJobs.First.GetByTestId(SettingsTestIds.ManuscriptJobNotes)).ToContainTextAsync("raw-LaTeX");
    }

    [Test, Order(6)]
    public async Task Step6_WhenWholeBookExportedAsMarkdown_ShouldHoldTheLibrarysWordCount()
    {
        await OpenArchiveAsync(ProductEngineering);
        await Settings.ExportManuscript.ClickAsync();
        await Settings.ManuscriptFormat("markdown").ClickAsync();

        var file = await SubmitAndDownloadAsync();

        (await ScalarSqlAsync<int>("SELECT markdown_word_count(@text)", ("text", Prose(file)))).ShouldBe(13703);
        var text = System.Text.Encoding.UTF8.GetString(file);
        Regex.Matches(text, @"^## ", RegexOptions.Multiline).Count.ShouldBe(10);
        text.ShouldNotContain("::");
    }
}
