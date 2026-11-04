using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.Contracts;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Print;

// Export manuscript (J19) as Word and EPUB on the seeded Product Engineering book. Pandoc converts the Markdown inside
// the print sandbox, so these need pandoc and codex-sandbox on PATH, as CI installs them (they fail where Landlock is
// unavailable, like the other print tests).
[TestFixture]
public partial class ManuscriptPandocApiTests : ExportApiFixture
{
    private static readonly string Book = $"/api/books/{SeedIds.Books.ProductEngineering}";

    [Test, Order(1)]
    public async Task Request_WhenWord_ShouldWriteHeadingsAndOneFootnotePerLiveUse()
    {
        var export = await ExportAsync(new ManuscriptExportRequest("docx"));

        export.Status.ShouldBe("succeeded", export.Error);
        export.Filename.ShouldBe("product-engineering.docx");
        using var zip = new ZipArchive(new MemoryStream(await Client.GetByteArrayAsync(export.FileUrl)));
        var document = Read(zip, "word/document.xml");
        Regex.Matches(document, "w:pStyle w:val=\"Heading2\"").Count.ShouldBe(10);
        Regex.Matches(document, "w:pStyle w:val=\"Heading1\"").Count.ShouldBe(4, "3 acts and the bibliography");
        document.ShouldNotContain("ref:");
        document.ShouldNotContain("::");
        await using var db = await OpenDatabaseAsync();
        var uses = await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_use WHERE book_id = @book AND archived_at IS NULL AND generation_id IS NULL",
            new { book = SeedIds.Books.ProductEngineering });
        FootnoteElement().Matches(Read(zip, "word/footnotes.xml")).Count(m => !m.Value.Contains("w:type=")).ShouldBe(uses);
    }

    [Test, Order(2)]
    public async Task Request_WhenEpubWithBeatsAndSummaries_ShouldBeAValidPackageWithAPageForEveryChapter()
    {
        var export = await ExportAsync(new ManuscriptExportRequest("epub", IncludeBeats: true, IncludeSummaries: true));

        export.Status.ShouldBe("succeeded", export.Error);
        using var zip = new ZipArchive(new MemoryStream(await Client.GetByteArrayAsync(export.FileUrl)));
        zip.Entries[0].FullName.ShouldBe("mimetype");
        zip.Entries.ShouldContain(e => e.FullName == "EPUB/nav.xhtml");
        var pages = zip.Entries.Where(e => Regex.IsMatch(e.FullName, @"^EPUB/text/ch\d+\.xhtml$")).ToList();
        pages.Count.ShouldBe(14, "10 chapters, 3 act pages and the bibliography");
        var text = string.Concat(pages.Select(p => Read(zip, p.FullName)));
        await using var db = await OpenDatabaseAsync();
        var beats = await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM beat b JOIN scene s ON s.id = b.scene_id WHERE s.book_id = @book AND b.archived_at IS NULL AND btrim(b.body) <> ''",
            new { book = SeedIds.Books.ProductEngineering });
        Regex.Matches(text, "<blockquote>\\s*<p>Beat:").Count.ShouldBe(beats);
    }

    private async Task<ExportDto> ExportAsync(ManuscriptExportRequest request)
    {
        using var response = await Client.PostAsJsonAsync($"{Book}/manuscript-exports", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return await SettledAsync((await response.Content.ReadFromJsonAsync<ExportDto>())!.Id);
    }

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [GeneratedRegex("<w:footnote [^>]*>")]
    private static partial Regex FootnoteElement();
}
