using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Import;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Lanes.Import;

// From the seed: a Markdown folder previewed and imported lands on the new book's Plan with the summary banner;
// Undo import asks, then deletes the book; a folder with stray files lists them as skipped, in the preview and on the
// banner; once the book is written in Undo is not offered; Dismiss hides the banner for good.
[TestFixture]
public partial class ImportTests : SeededAcceptanceTest
{
    private static readonly string SmallFolder = Path.Combine(RepoPaths.Root, "src", "Codex.TestSupport", "Fixtures", "import", "markdown-small");

    private readonly string _badFolder = Path.Combine(Path.GetTempPath(), $"codex-import-{Guid.NewGuid():N}", "markdown-bad");

    private string _bookId = "";

    private ImportScreen Import => new(Page);

    [OneTimeSetUp]
    public void WriteBadFolder()
    {
        foreach (var file in Directory.EnumerateFiles(SmallFolder, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_badFolder, Path.GetRelativePath(SmallFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        File.WriteAllText(Path.Combine(_badFolder, "manuscript", "cover.png"), "not an image");
        File.WriteAllText(Path.Combine(_badFolder, "manuscript", "todo.md"), "- finish act 2");
        File.WriteAllText(Path.Combine(_badFolder, "manuscript", "act-02-the-road", "chapter-03-crossing", "scene-09-prose.md"), "Lost prose.");
    }

    [OneTimeTearDown]
    public void DeleteBadFolder() => Directory.Delete(Path.GetDirectoryName(_badFolder)!, recursive: true);

    [Test, Order(1)]
    public async Task Import_WhenMarkdownFolder_ShouldLandOnPlanWithTheSummary()
    {
        await Page.GotoAsync(Links.Library());

        var preview = await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST", () => Import.PickFolderAsync(SmallFolder));

        preview.Status.ShouldBe(200);
        await Expect(Import.PreviewTitle).ToHaveTextAsync("The Lantern Road");
        await Expect(Import.PreviewRow("Acts")).ToContainTextAsync("2");
        await Expect(Import.PreviewRow("Scene beats")).ToContainTextAsync("6");
        await Expect(Import.SkippedRows).ToHaveCountAsync(0);

        var created = await Page.RunAndWaitForApiAsync("/api/imports", "POST", () => Import.Run.ClickAsync());

        created.Status.ShouldBe(201);
        await Expect(Page).ToHaveURLAsync(PlanUrl());
        _bookId = PlanUrl().Match(Page.Url).Groups[1].Value;
        await Expect(Import.Banner).ToBeVisibleAsync();
        await Expect(Import.BannerText).ToContainTextAsync("2 acts, 3 chapters, 3 scenes and");
        await Expect(Import.BannerText).ToContainTextAsync("plus 3 Knowledge base entries.");
        await Expect(Import.BannerSkipped).ToHaveCountAsync(0);
    }

    [Test, Order(2)]
    public async Task Undo_WhenClicked_ShouldDeleteTheBookAndReturnToTheLibrary()
    {
        await Page.GotoAsync(Links.Plan(_bookId));
        await Expect(Import.Undo).ToBeVisibleAsync();

        await Import.Undo.ClickAsync();
        await Expect(Import.UndoConfirm).ToContainTextAsync("Undo import deletes the book and everything in it");

        var undone = await Page.RunAndWaitForApiAsync($"/api/imports/{(await ImportIdAsync())}/undo", "POST", () => Import.UndoConfirmDelete.ClickAsync());

        undone.Status.ShouldBe(204);
        await Expect(Page).ToHaveURLAsync(new Regex(@"/$"));
        (await Page.APIRequest.GetAsync($"/api/books/{_bookId}/import")).Status.ShouldBe(404);
    }

    [Test, Order(3)]
    public async Task Import_WhenFolderHasStrayFiles_ShouldReportSkippedItems()
    {
        await Page.GotoAsync(Links.Library());

        await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST", () => Import.PickFolderAsync(_badFolder));

        await Expect(Import.SkippedRows).ToHaveCountAsync(3);
        await Expect(Import.SkippedRows.Filter(new() { HasText = "cover.png" })).ToContainTextAsync("Not a Markdown file");
        await Expect(Import.SkippedRows.Filter(new() { HasText = "scene-09-prose.md" })).ToContainTextAsync("No scene file for this prose");

        await Page.RunAndWaitForApiAsync("/api/imports", "POST", () => Import.Run.ClickAsync());

        await Expect(Page).ToHaveURLAsync(PlanUrl());
        await Expect(Import.BannerSkipped).ToHaveTextAsync("3 items were skipped");
        _bookId = PlanUrl().Match(Page.Url).Groups[1].Value;
        await Import.BannerSkipped.ClickAsync();
        await Expect(Import.BannerSkippedRows.Filter(new() { HasText = "todo.md" })).ToContainTextAsync("Not part of the import layout");
    }

    [Test, Order(4)]
    public async Task Undo_WhenTheBookWasWrittenInSinceTheImport_ShouldNotBeOffered()
    {
        var outline = (await (await Page.APIRequest.GetAsync($"/api/books/{_bookId}/outline")).JsonAsync())!.Value;
        var sceneId = outline.GetProperty("acts")[1].GetProperty("chapters")[0].GetProperty("scenes")[0].GetProperty("id").GetString();
        var prose = (await (await Page.APIRequest.GetAsync($"/api/scenes/{sceneId}/prose")).JsonAsync())!.Value;
        var saved = await Page.APIRequest.PutAsync($"/api/scenes/{sceneId}/prose", new()
        {
            DataObject = new { body = prose.GetProperty("body").GetString() + "\nA new line.\n", version = prose.GetProperty("version").GetInt32() },
        });
        saved.Status.ShouldBe(200);

        await Page.GotoAsync(Links.Plan(_bookId));

        await Expect(Import.Banner).ToBeVisibleAsync();
        await Expect(Import.Undo).ToHaveCountAsync(0);
    }

    [Test, Order(5)]
    public async Task Dismiss_WhenClicked_ShouldHideTheBannerForGood()
    {
        await Page.GotoAsync(Links.Plan(_bookId));
        await Expect(Import.Banner).ToBeVisibleAsync();

        var dismissed = await Page.RunAndWaitForApiAsync($"/api/imports/{(await ImportIdAsync())}/dismiss", "POST", () => Import.Dismiss.ClickAsync());

        dismissed.Status.ShouldBe(204);
        await Expect(Import.Banner).ToHaveCountAsync(0);
        await Page.RunAndWaitForApiAsync($"/api/books/{_bookId}/import", "GET", () => Page.ReloadAsync());
        await Expect(Import.Banner).ToHaveCountAsync(0);
    }

    private async Task<string> ImportIdAsync()
    {
        var response = await Page.APIRequest.GetAsync($"/api/books/{_bookId}/import");
        return (await response.JsonAsync())!.Value.GetProperty("banner").GetProperty("importId").GetString()!;
    }

    [GeneratedRegex(@"/books/([0-9a-f-]{36})/plan$")]
    private static partial Regex PlanUrl();
}
