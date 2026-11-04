using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.Contracts;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Print;

// Export manuscript (J19) on the seeded Product Engineering book, Markdown only: the worker writes the file without
// pandoc, so this runs anywhere. Word and EPUB are ManuscriptPandocApiTests.
[TestFixture]
public partial class ManuscriptExportApiTests : ExportApiFixture
{
    private static readonly string Book = $"/api/books/{SeedIds.Books.ProductEngineering}";

    [Test, Order(1)]
    public async Task Info_WhenTheBookHasChapters_ShouldListThemInOrderWithTheAtRiskCount()
    {
        var info = (await Client.GetFromJsonAsync<ManuscriptExportInfoDto>($"{Book}/manuscript-export"))!;

        info.Chapters.Count.ShouldBe(10);
        info.Chapters[0].Number.ShouldBe(1);
        info.Chapters[5].Id.ShouldBe(SeedIds.Chapters.Ch6);
        info.AtRiskSources.ShouldBe(3);
    }

    [Test, Order(2)]
    public async Task Request_WhenWholeBookMarkdown_ShouldWriteAReadableFileWithFootnotesAndNoDirectives()
    {
        var export = await ExportAsync(new ManuscriptExportRequest("markdown"));

        export.Status.ShouldBe("succeeded", export.Error);
        (export.Kind, export.Filename).ShouldBe(("markdown", "product-engineering.md"));
        var text = await Client.GetStringAsync(export.FileUrl);
        Lines(text, @"^## ").Count.ShouldBe(10);
        Lines(text, @"^# (?!Bibliography)").Count.ShouldBe(3);
        text.ShouldNotContain("::");
        text.ShouldNotContain("(ref:");
        text.ShouldNotContain("(kb:");
        text.ShouldNotContain("(chapter:");
        text.ShouldContain("# Bibliography");
        await using var db = await OpenDatabaseAsync();
        var uses = await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM reference_use WHERE book_id = @book AND archived_at IS NULL AND generation_id IS NULL",
            new { book = SeedIds.Books.ProductEngineering });
        FootnoteDefinition().Matches(text).Count.ShouldBe(uses);
        (await db.ExecuteScalarAsync<int>("SELECT markdown_word_count(@text)", new { text = Prose(text) })).ShouldBe(13703);
    }

    [Test, Order(3)]
    public async Task Request_WhenOneChapterMarkdown_ShouldHoldOnlyThatChapter()
    {
        var export = await ExportAsync(new ManuscriptExportRequest("markdown", "chapter", SeedIds.Chapters.Ch6));

        export.Status.ShouldBe("succeeded", export.Error);
        var text = await Client.GetStringAsync(export.FileUrl);
        Lines(text, @"^## ").ShouldHaveSingleItem().ShouldContain("Chapter 6");
        Lines(text, @"^# (?!Bibliography)").ShouldBeEmpty();
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT markdown_word_count(@text)", new { text = Prose(text) })).ShouldBe(640);
    }

    [Test, Order(4)]
    public async Task Request_WhenBeatsAndSummariesAreOn_ShouldQuoteEachBeatAndItaliciseEachSummary()
    {
        var export = await ExportAsync(new ManuscriptExportRequest("markdown", IncludeBeats: true, IncludeSummaries: true));

        export.Status.ShouldBe("succeeded", export.Error);
        var text = await Client.GetStringAsync(export.FileUrl);
        await using var db = await OpenDatabaseAsync();
        var beats = await db.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM beat b JOIN scene s ON s.id = b.scene_id WHERE s.book_id = @book AND b.archived_at IS NULL AND btrim(b.body) <> ''",
            new { book = SeedIds.Books.ProductEngineering });
        Lines(text, @"^> Beat: ").Count.ShouldBe(beats);
        beats.ShouldBeGreaterThan(0);
        Lines(text, @"^\*[^*\s].*\*$").ShouldNotBeEmpty();
    }

    [Test, Order(5)]
    public async Task Request_WhenPlayersGuide_ShouldListWhatItSkipped()
    {
        var export = await ExportAsync(new ManuscriptExportRequest("markdown"), $"/api/books/{SeedIds.Books.PlayersGuide}");

        export.Status.ShouldBe("succeeded", export.Error);
        export.Notes.ShouldNotBeNull().ShouldContain(n => n.Contains("raw-LaTeX"));
        (await Client.GetStringAsync(export.FileUrl)).ShouldNotContain("```{=latex}");
    }

    [Test, Order(6)]
    public async Task Request_WhenTheBookHasNoChapters_ShouldBeBlockedWithoutAJob()
    {
        var northbound = $"/api/books/{SeedIds.Books.Northbound}";
        using var response = await Client.PostAsJsonAsync($"{northbound}/manuscript-exports", new ManuscriptExportRequest("markdown"));

        var error = await response.ReadApiErrorAsync(HttpStatusCode.UnprocessableEntity);
        (error.Code, error.Error).ShouldBe((ApiErrorCodes.Blocked, "Nothing to export. This book has no chapters."));
        await using var db = await OpenDatabaseAsync();
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM export_job WHERE book_id = @book", new { book = SeedIds.Books.Northbound })).ShouldBe(0);
    }

    [Test, Order(7)]
    public async Task Request_WhenSettingsAreWrong_ShouldAnswer400Or404()
    {
        using var format = await Client.PostAsJsonAsync($"{Book}/manuscript-exports", new ManuscriptExportRequest("pdf"));
        using var scope = await Client.PostAsJsonAsync($"{Book}/manuscript-exports", new ManuscriptExportRequest("markdown", "chapter"));
        using var chapter = await Client.PostAsJsonAsync($"{Book}/manuscript-exports", new ManuscriptExportRequest("markdown", "chapter", Guid.NewGuid()));

        (await format.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await scope.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe(ApiErrorCodes.Validation);
        (await chapter.ReadApiErrorAsync(HttpStatusCode.NotFound)).Code.ShouldBe(ApiErrorCodes.NotFound);
    }

    [Test, Order(8)]
    public async Task Request_WhenOneOfTheKindIsAlreadyQueued_ShouldConflict()
    {
        await using (var db = await OpenDatabaseAsync())
        {
            await db.ExecuteAsync(
                "INSERT INTO export_job (book_id, kind, settings, status) VALUES (@book, 'epub', '{}', 'running')",
                new { book = SeedIds.Books.HymnsForMachines });
        }

        using var response = await Client.PostAsJsonAsync($"/api/books/{SeedIds.Books.HymnsForMachines}/manuscript-exports", new ManuscriptExportRequest("epub"));

        (await response.ReadApiErrorAsync(HttpStatusCode.Conflict)).Code.ShouldBe(ApiErrorCodes.Conflict);
    }

    [Test, Order(9)]
    public async Task List_WhenExportsFinished_ShouldListManuscriptKindsOnly()
    {
        var list = (await Client.GetFromJsonAsync<ExportListDto>($"{Book}/manuscript-exports"))!;
        var printList = (await Client.GetFromJsonAsync<ExportListDto>($"{Book}/exports"))!;

        list.Exports.ShouldNotBeEmpty();
        list.Exports.ShouldAllBe(e => e.Kind == "markdown");
        printList.Exports.ShouldNotContain(e => e.Kind == "markdown");
    }

    private async Task<ExportDto> ExportAsync(ManuscriptExportRequest request, string? book = null)
    {
        using var response = await Client.PostAsJsonAsync($"{book ?? Book}/manuscript-exports", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return await SettledAsync((await response.Content.ReadFromJsonAsync<ExportDto>())!.Id);
    }

    private static List<string> Lines(string text, string pattern) =>
        text.Split('\n').Where(l => Regex.IsMatch(l, pattern)).ToList();

    // The scene text only: no title block, headings, footnote markers or definitions, bibliography.
    private static string Prose(string text)
    {
        var body = text[(text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + 5)..];
        var end = Regex.Match(body, @"^(# Bibliography|\[\^\d+\]:)", RegexOptions.Multiline);
        body = end.Success ? body[..end.Index] : body;
        body = Regex.Replace(body, @"^#{1,6} .*$", string.Empty, RegexOptions.Multiline);
        return Regex.Replace(body, @"\[\^\d+\]", string.Empty);
    }

    [GeneratedRegex(@"^\[\^\d+\]: ", RegexOptions.Multiline)]
    private static partial Regex FootnoteDefinition();
}
