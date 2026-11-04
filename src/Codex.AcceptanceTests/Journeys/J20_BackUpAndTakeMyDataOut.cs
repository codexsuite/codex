using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Import;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.References;

namespace Codex.AcceptanceTests.Journeys;

// J20 · Back up and take my data out (docs/journeys-wave5.md): Product Engineering → Book settings → Export source builds the
// backup zip, Library → Import → Codex backup reads it, and the restored book is the original less the pending AI draft the
// backup leaves out. The negative paths are J20_BackUpAndTakeMyDataOut_Negative.
[TestFixture, Category("J20")]
public partial class J20_BackUpAndTakeMyDataOut : JourneyTest
{
    private static readonly string ProductEngineering = SeedIds.Books.ProductEngineering.ToString();
    private static readonly LocatorAssertionsToHaveTextOptions Built = new() { Timeout = 120_000 };

    private static byte[] _zip = [];
    private static string _restored = string.Empty;

    private SettingsScreen Settings => new(Page);

    private ImportScreen Import => new(Page);

    private async Task<T> GetAsync<T>(string url)
    {
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return (await api.GetFromJsonAsync<T>(url))!;
    }

    [Test, Order(1)]
    public async Task Step1_WhenExportSourceClicked_ShouldBuildTheBackupAndSayWhatItLeftOut()
    {
        await Page.GotoAsync(Links.Settings(ProductEngineering, Links.Hash(("archive", null))));

        await Settings.ExportSource.ClickAsync();

        await Expect(Settings.ExportStatus).ToHaveTextAsync(new Regex("^Export succeeded\\. Download$"), Built);
        await Expect(Settings.ExportNotes).ToContainTextAsync("1 pending AI draft is not included");
        await Expect(Settings.ExportNotes).ToContainTextAsync("0 images not included");
        await Expect(Settings.ExportSource).ToBeEnabledAsync();
        (await ScalarSqlAsync<string>("SELECT status FROM export_job WHERE kind = 'source'")).ShouldBe("succeeded");
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        _zip = await api.GetByteArrayAsync((await Settings.ExportDownload.GetAttributeAsync("href"))!);
        using var zip = new ZipArchive(new MemoryStream(_zip));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        names.Count(n => n.StartsWith("manuscript/", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)).ShouldBe(15);
        names.ShouldContain("book.json");
        names.ShouldContain("knowledge-base.json");
        names.ShouldContain("references.json");
        names.ShouldContain("labels.json");
    }

    [Test, Order(2)]
    public async Task Step2_WhenTheBackupIsPickedInImport_ShouldCountWhatItHolds()
    {
        await Page.GotoAsync(Links.Library());

        var preview = await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST",
            () => Import.PickFileAsync("codex_zip", "product-engineering.zip", "application/zip", _zip));

        preview.Status.ShouldBe(200);
        await Expect(Import.PreviewTitle).ToHaveTextAsync("Product Engineering");
        await Expect(Import.Preview).ToContainTextAsync("13,703 words");
        await Expect(Import.PreviewRow("Chapters")).ToContainTextAsync("10");
        await Expect(Import.PreviewRow("Scenes")).ToContainTextAsync("15");
        await Expect(Import.PreviewRow("Knowledge base entries")).ToContainTextAsync("12");
        await Expect(Import.PreviewRow("Sources")).ToContainTextAsync("12");
        await Expect(Import.PreviewRow("Cited sources")).ToContainTextAsync("10");
        await Expect(Import.PreviewRow("Labels")).ToContainTextAsync("4");
    }

    [Test, Order(3)]
    public async Task Step3_WhenTheBackupIsRestored_ShouldOpenPlanOfARestoredBookWithTheBanner()
    {
        await Page.GotoAsync(Links.Library());
        await Page.RunAndWaitForApiAsync("/api/imports/preview", "POST",
            () => Import.PickFileAsync("codex_zip", "product-engineering.zip", "application/zip", _zip));

        var created = await Page.RunAndWaitForApiAsync("/api/imports", "POST", () => Import.Run.ClickAsync());

        created.Status.ShouldBe(201);
        await Expect(Page).ToHaveURLAsync(PlanUrl());
        _restored = PlanUrl().Match(Page.Url).Groups[1].Value;
        await Expect(Import.Banner).ToBeVisibleAsync();
        await Expect(Import.BannerText).ToContainTextAsync("3 acts, 10 chapters, 15 scenes and 13,703 words");
        await Expect(Import.BannerText).ToContainTextAsync("12 Knowledge base entries, 12 sources (10 cited) and 4 labels");
        (await ScalarSqlAsync<string>("SELECT title FROM book WHERE id = @id", ("id", Guid.Parse(_restored)))).ShouldBe("Product Engineering (restored)");
        (await ScalarSqlAsync<string>("SELECT source FROM book_import WHERE book_id = @id", ("id", Guid.Parse(_restored)))).ShouldBe("codex_zip");
    }

    [Test, Order(4)]
    public async Task Step4_WhenTheRestoredBookIsChecked_ShouldMatchTheOriginalLessThePendingDraft()
    {
        var restored = Guid.Parse(_restored);
        (await ScalarSqlAsync<int>("SELECT word_count FROM book_stats WHERE book_id = @id", ("id", restored))).ShouldBe(13703);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM chapter WHERE book_id = @id", ("id", restored))).ShouldBe(10);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM scene WHERE book_id = @id AND archived_at IS NULL", ("id", restored))).ShouldBe(15);

        var summary = await GetAsync<ClearanceSummaryDto>($"/api/books/{_restored}/references/summary");
        // The original reads 4 cleared of 11 cited; the backup leaves out the pending draft, whose uses reopened Accelerate.
        (summary.Cleared, summary.Total, summary.NotCited, summary.Risks, summary.Badge).ShouldBe((5, 12, 2, 3, 3));
        (await ScalarSqlAsync<long>(
            """
            SELECT count(*) FROM permission_request p JOIN reference r ON r.id = p.reference_id
            WHERE r.book_id = @id AND p.status = 'sent'
            """, ("id", restored))).ShouldBe(1);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_entry WHERE book_id = @id", ("id", restored))).ShouldBe(12);

        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(_restored));
        await Expect(write.Markers).ToHaveCountAsync(12);
        (await ScalarSqlAsync<long>(
            """
            SELECT count(*) FROM reference_use u WHERE u.book_id = @id AND u.archived_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM prose p WHERE p.scene_id = u.scene_id AND strpos(p.body, '(ref:' || u.id || ')') > 0)
            """, ("id", restored))).ShouldBe(0);
    }

    [Test, Order(5)]
    public async Task Step5_WhenTheBackupIsRestored_ShouldLeaveTheSourceBookUnchanged()
    {
        (await ScalarSqlAsync<long>("SELECT count(*) FROM prose p JOIN scene s ON s.id = p.scene_id WHERE s.book_id = @id AND p.version > 1", ("id", SeedIds.Books.ProductEngineering))).ShouldBe(0);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineering))).ShouldBe(12);
        (await ScalarSqlAsync<int>("SELECT word_count FROM book_stats WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineering))).ShouldBe(13703);
    }

    [GeneratedRegex(@"/books/([0-9a-f-]{36})/plan$")]
    private static partial Regex PlanUrl();
}
