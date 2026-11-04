using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Import;
using Codex.TestSupport.Fixtures;

namespace Codex.AcceptanceTests.Lanes.Import;

// From the seed: the Product Engineering (v1) Novelcrafter export and Word document, picked as single files, preview and
// land on the new book's Plan with the summary; the Word document lists its images and comments as skipped.
[TestFixture]
public partial class ImportArchiveTests : SeededAcceptanceTest
{
    private ImportScreen Import => new(Page);

    [Test, Order(1)]
    public async Task Import_WhenNovelcrafterZip_ShouldLandOnPlanWithEntries()
    {
        await Page.GotoAsync(Links.Library());

        var preview = await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST",
            () => Import.PickFileAsync("novelcrafter", "product-engineering-v1.zip", "application/zip", ProductEngineeringV1.NovelcrafterZip()));

        preview.Status.ShouldBe(200);
        await Expect(Import.PreviewTitle).ToHaveTextAsync("Product Engineering");
        await Expect(Import.PreviewRow("Knowledge base entries")).ToContainTextAsync("4");
        await Expect(Import.SkippedRows).ToHaveCountAsync(1);

        var created = await Page.RunAndWaitForApiAsync("/api/imports", "POST", () => Import.Run.ClickAsync());

        created.Status.ShouldBe(201);
        await Expect(Page).ToHaveURLAsync(PlanUrl());
        await Expect(Import.BannerText).ToContainTextAsync("2 acts, 3 chapters, 4 scenes and");
        await Expect(Import.BannerText).ToContainTextAsync("plus 4 Knowledge base entries.");
    }

    [Test, Order(2)]
    public async Task Import_WhenDocx_ShouldLandOnPlanAndListWhatItLeftOut()
    {
        await Page.GotoAsync(Links.Library());

        var preview = await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST",
            () => Import.PickFileAsync("docx", "Product Engineering.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ProductEngineeringV1.Docx()));

        preview.Status.ShouldBe(200);
        await Expect(Import.PreviewRow("Chapters")).ToContainTextAsync("4");
        await Expect(Import.SkippedRows.Filter(new() { HasText = "Images" })).ToContainTextAsync("1 image is not imported");

        await Page.RunAndWaitForApiAsync("/api/imports", "POST", () => Import.Run.ClickAsync());

        await Expect(Page).ToHaveURLAsync(PlanUrl());
        await Expect(Import.BannerText).ToContainTextAsync("1 act, 4 chapters, 6 scenes and");
        await Expect(Import.BannerSkipped).ToHaveTextAsync("2 items were skipped");
    }

    [GeneratedRegex(@"/books/([0-9a-f-]{36})/plan$")]
    private static partial Regex PlanUrl();
}
