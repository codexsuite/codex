using System.Net;
using System.Net.Http.Json;
using Codex.Application.Features.Import;
using Codex.Contracts.Import;
using Codex.TestSupport.Fixtures;
using Dapper;

namespace Codex.WebApi.Tests.Import;

// Uploads the importer refuses (400, nothing written) or takes with skipped items.
[TestFixture]
public class ImportValidationTests : ApiFixture
{
    private static readonly (string, byte[])[] Layout =
    [
        ImportForms.Text("b/manuscript/index.md", "---\ntitle: Small\n---\n"),
        ImportForms.Text("b/manuscript/act-01-one/chapter-01-a/scene-01.md", "---\nid: s1\n---\n"),
    ];

    [Test]
    public async Task Import_WhenFolderEmpty_ShouldAnswer400AndWriteNothing()
    {
        (await RefusedAsync(ImportForms.Form([]))).ShouldContain("empty", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenNoMarkdown_ShouldAnswer400()
    {
        (await RefusedAsync(ImportForms.Form([ImportForms.Text("pics/cover.png", "png"), ImportForms.Text("pics/notes.txt", "x")]))).ShouldContain("no Markdown", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenMarkdownWithoutTheLayout_ShouldAnswer400()
    {
        (await RefusedAsync(ImportForms.Form([ImportForms.Text("notes/one.md", "# One"), ImportForms.Text("notes/two.md", "# Two")]))).ShouldContain("no act-NN folders", Case.Insensitive);
    }

    [TestCase("../escape/act-01-x/index.md")]
    [TestCase("b/../../etc/act-01-x/index.md")]
    [TestCase("/etc/act-01-x/index.md")]
    [TestCase("b\\act-01-x\\index.md")]
    [TestCase("C:/b/act-01-x/index.md")]
    [TestCase("b//act-01-x/index.md")]
    [TestCase("b/./act-01-x/index.md")]
    public async Task Import_WhenAPathLeavesTheFolder_ShouldAnswer400(string path)
    {
        (await RefusedAsync(ImportForms.Form([.. Layout, ImportForms.Text(path, "x")]))).ShouldContain("not a file name inside the folder", Case.Insensitive);
    }

    [Test]
    public async Task Preview_WhenAPathLeavesTheFolder_ShouldAnswer400()
    {
        var response = await Client.PostAsync("/api/imports/preview", ImportForms.Form([.. Layout, ImportForms.Text("../x.md", "x")]));

        (await response.ReadApiErrorAsync(HttpStatusCode.BadRequest)).Code.ShouldBe("validation");
    }

    [Test]
    public async Task Import_WhenSourceUnknown_ShouldAnswer400()
    {
        (await RefusedAsync(ImportForms.Form(Layout, source: "scrivener"))).ShouldContain("not available", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenNotMultipart_ShouldAnswer400()
    {
        (await RefusedAsync(JsonContent.Create(new { source = "markdown" }))).ShouldContain("multipart", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenTooManyFiles_ShouldAnswer400()
    {
        var files = Enumerable.Range(0, ImportLimits.MaxFiles + 1).Select(i => ImportForms.Text($"b/research/n{i}.md", "x"));

        (await RefusedAsync(ImportForms.Form([.. Layout, .. files]))).ShouldContain("at most", Case.Insensitive);
    }

    [Test]
    public async Task Import_WhenMoreFilesThanTheFormReaderTakes_ShouldAnswer400()
    {
        var files = Enumerable.Range(0, 2100).Select(i => ImportForms.Text($"b/research/n{i}.md", "x"));

        (await RefusedAsync(ImportForms.Form([.. Layout, .. files]))).ShouldContain("at most 2000 files");
    }

    [Test]
    public async Task Import_WhenTotalOverTheSizeCap_ShouldAnswer400()
    {
        var files = Enumerable.Range(0, 13).Select(i => ($"b/research/big{i}.md", new byte[ImportLimits.MaxFileBytes]));

        (await RefusedAsync(ImportForms.Form([.. Layout, .. files]))).ShouldContain("at most 25 MB");
    }

    [Test]
    public async Task Import_WhenAnOmittedPathLeavesTheFolder_ShouldAnswer400()
    {
        (await RefusedAsync(ImportForms.Form(Layout, notMarkdown: ["../secret.png"]))).ShouldContain("not a file name inside the folder");
    }

    [Test]
    public async Task Preview_WhenTheClientLeftFilesOut_ShouldListThemAsSkipped()
    {
        var response = await Client.PostAsync("/api/imports/preview",
            ImportForms.Form(Layout, notMarkdown: ["b/manuscript/cover.png"], tooLarge: ["b/manuscript/act-01-one/chapter-01-a/scene-09.md"]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = (await response.Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        preview.FileCount.ShouldBe(4);
        preview.Skipped.Select(s => (s.Path, s.Reason)).ShouldBe([
            ("b/manuscript/cover.png", "Not a Markdown file"),
            ("b/manuscript/act-01-one/chapter-01-a/scene-09.md", "Too large (over 2 MB)"),
        ]);
    }

    [Test]
    public async Task Import_WhenAFileIsTooLargeOrNotMarkdownOrStray_ShouldSkipItAndImportTheRest()
    {
        var big = ("b/manuscript/act-01-one/chapter-01-a/scene-02.md", new byte[ImportLimits.MaxFileBytes + 1]);
        var files = new[]
        {
            big,
            ImportForms.Text("b/manuscript/act-01-one/cover.png", "png"),
            ImportForms.Text("b/manuscript/act-01-one/chapter-01-a/draft-notes.md", "notes"),
            ImportForms.Text("b/manuscript/act-01-one/chapter-01-a/scene-07-prose.md", "Orphan prose."),
            ImportForms.Text("b/elsewhere.md", "x"),
        };

        var response = await Client.PostAsync("/api/imports", ImportForms.Form([.. Layout, .. files]));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var summary = (await response.Content.ReadFromJsonAsync<ImportSummaryDto>())!;
        summary.Scenes.ShouldBe(1);
        summary.Skipped.Select(s => (s.Path, s.Reason)).Order().ShouldBe(new[]
        {
            (big.Item1, "Too large (over 2 MB)"),
            ("b/manuscript/act-01-one/cover.png", "Not a Markdown file"),
            ("b/manuscript/act-01-one/chapter-01-a/draft-notes.md", "Not part of the import layout"),
            ("b/manuscript/act-01-one/chapter-01-a/scene-07-prose.md", "No scene file for this prose"),
            ("b/elsewhere.md", "Outside the manuscript folder"),
        }.Order());
    }

    // Asserts 400 validation and that no book was written; returns the error sentence.
    private async Task<string> RefusedAsync(HttpContent body)
    {
        await using var db = await OpenDatabaseAsync();
        var before = await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book");

        var response = await Client.PostAsync("/api/imports", body);

        var error = await response.ReadApiErrorAsync(HttpStatusCode.BadRequest);
        error.Code.ShouldBe("validation");
        (await db.ExecuteScalarAsync<int>("SELECT count(*)::int FROM book")).ShouldBe(before);
        return error.Error;
    }
}
