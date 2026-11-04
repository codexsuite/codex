using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// Novelcrafter and .docx uploads the importers refuse: 400, and nothing is written.
[TestFixture]
public class ImportArchiveValidationTests : ApiFixture
{
    private static MultipartFormDataContent Zip(string name, byte[] content) => ImportForms.Form([(name, content)], source: "novelcrafter");

    private static MultipartFormDataContent Docx(string name, byte[] content) => ImportForms.Form([(name, content)], source: "docx");

    [Test]
    public async Task Import_WhenZipIsCorrupt_ShouldAnswer400AndWriteNothing()
    {
        var zip = ProductEngineeringV1.NovelcrafterZip();

        (await RefusedAsync(Zip("broken.zip", zip[..(zip.Length / 2)]))).ShouldContain("not a readable", Case.Insensitive);
        (await RefusedAsync(Zip("text.zip", "just some text"u8.ToArray()))).ShouldContain("not a readable", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenZipEntryEscapesTheArchive_ShouldAnswer400()
    {
        var zip = TestZip.Of(("../../evil.md", "x"), ("act-01-a/chapter-01-b/scene-01.md", "---\nid: s1\n---\n"));

        (await RefusedAsync(Zip("slip.zip", zip))).ShouldContain("not a path inside it");
    }

    [Test]
    public async Task Preview_WhenZipEntryEscapesTheArchive_ShouldAnswer400()
    {
        var response = await Client.PostAsync("/api/imports/preview", Zip("slip.zip", TestZip.Of(("a/../../evil.md", "x"))));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
    }

    [Test]
    public async Task Import_WhenZipHasNoManuscript_ShouldAnswer400()
    {
        (await RefusedAsync(Zip("notes.zip", TestZip.Of(("notes/one.md", "# One"))))).ShouldContain("no manuscript", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenUploadIsOverTheSizeCap_ShouldAnswer400()
    {
        (await RefusedAsync(Zip("huge.zip", new byte[(int)ImportLimits.MaxTotalBytes + 1024 * 1024]))).ShouldContain("at most 25 MB");
        (await RefusedAsync(Docx("huge.docx", new byte[(int)ImportLimits.MaxTotalBytes + 1024 * 1024]))).ShouldContain("at most 25 MB");
    }

    [Test]
    public async Task Import_WhenZipExpandsFarBeyondItsSize_ShouldAnswer400()
    {
        var zeros = new string('0', ImportLimits.MaxFileBytes - 10);
        var bomb = TestZip.Of(Enumerable.Range(1, 6).Select(n => ($"act-01-a/chapter-01-b/scene-{n:00}.md", zeros)).ToArray());

        (await RefusedAsync(Zip("bomb.zip", bomb))).ShouldContain("expands far beyond");
    }

    [Test]
    public async Task Import_WhenDocxHasNoHeadings_ShouldAnswer400()
    {
        var docx = new DocxBuilder().Title("Notes").Paragraph("Just text, no chapters.").Build();

        (await RefusedAsync(Docx("notes.docx", docx))).ShouldContain("no Heading 1");
    }

    [Test]
    public async Task Import_WhenDocxNestsThousandsOfElementsDeep_ShouldAnswer400AndWriteNothing()
    {
        var deep = DocxBuilder.DeeplyNested(20_000);

        deep.Length.ShouldBeLessThan(16 * 1024);
        (await RefusedAsync(Docx("deep.docx", deep))).ShouldContain("nested more than 256 levels");
        // The server is still up: a normal upload after it works.
        (await Client.PostAsync("/api/imports/preview", Docx("ok.docx", ProductEngineeringV1.Docx()))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Import_WhenDocxSceneIsOverTheProseLimit_ShouldImportTheRestAndListTheSceneAsTooLarge()
    {
        var random = new Random(3);
        var half = string.Concat(Enumerable.Range(0, (ImportLimits.MaxFileBytes / 2) + 1).Select(_ => (char)('a' + random.Next(26))));
        var docx = new DocxBuilder().Heading(1, "Big").Paragraph(half).Paragraph(half).Heading(1, "Small").Paragraph("Fine.").Build();

        var response = await Client.PostAsync("/api/imports", Docx("big.docx", docx));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var summary = (await response.Content.ReadFromJsonAsync<Codex.Contracts.Import.ImportSummaryDto>())!;
        summary.Chapters.ShouldBe(2);
        summary.Words.ShouldBe(1);
        summary.Skipped.Select(s => (s.Path, s.Reason)).ShouldBe([("Big, scene 1", ImportLimits.TooLarge)]);
    }

    [Test]
    public async Task Import_WhenDocxIsNotAWordDocument_ShouldAnswer400()
    {
        (await RefusedAsync(Docx("fake.docx", "plain text"u8.ToArray()))).ShouldContain("not a readable", Case.Insensitive);
        (await RefusedAsync(Docx("zip.docx", TestZip.Of(("readme.txt", "x"))))).ShouldContain("not a Word document", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenSourceAndFileDisagree_ShouldAnswer400()
    {
        (await RefusedAsync(Docx("book.zip", ProductEngineeringV1.NovelcrafterZip()))).ShouldContain(".docx");
        (await RefusedAsync(ImportForms.Form([("a.zip", [1]), ("b.zip", [2])], source: "novelcrafter"))).ShouldContain("one", Case.Insensitive);
    }

    private async Task<string> RefusedAsync(HttpContent form)
    {
        await using var db = await OpenDatabaseAsync();
        var books = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book");

        var response = await Client.PostAsync("/api/imports", form);

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe("validation");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book")).ShouldBe(books);
        return error.Error;
    }
}
