using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Layout;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;
using Codex.Contracts.Print;

namespace Codex.AcceptanceTests.Lanes.Layout;

// Export for printer on the seed's Players Guide: its image has no file, so a final export is blocked and only a draft
// goes through; Product Engineering's copyright risks block too. The draft is built by the worker with a bleed, the job
// survives a reload, the history lists it with its settings and the PDF and the source .zip download, and Settings →
// Layout & print changes the trim the Layout view uses. Needs pandoc, tectonic and codex-sandbox on PATH, as CI installs them.
[TestFixture]
public class LayoutExportTests : SeededAcceptanceTest
{
    private static readonly string Guide = SeedIds.Books.PlayersGuide.ToString();
    private static readonly string Risky = SeedIds.Books.ProductEngineering.ToString();
    private static readonly LocatorAssertionsToBeVisibleOptions Build = new() { Timeout = 120_000 };
    private static readonly Regex Pages = new(@"^[1-9]\d* pages?$");

    private LayoutScreen Layout => new(Page);

    private async Task OpenExportAsync(string book)
    {
        await Page.GotoAsync(Links.Layout(book));
        await Layout.ExportOpen.ClickAsync();
        await Expect(Layout.ExportDialog).ToBeVisibleAsync();
    }

    private async Task OpenExportsTabAsync()
    {
        await Page.GotoAsync(Links.Layout(Guide));
        await Layout.Tab("exports").ClickAsync();
        await Expect(Layout.ExportsList).ToBeVisibleAsync();
    }

    private async Task<byte[]> DownloadAsync(string url)
    {
        using var api = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        return await api.GetByteArrayAsync(url);
    }

    [Test, Order(1)]
    public async Task Export_WhenCopyrightRisksBlock_ShouldDisableQueueUntilTheExportIsADraft()
    {
        await OpenExportAsync(Risky);

        await Expect(Layout.ExportVerdict).ToHaveTextAsync("3 issues block export");
        await Expect(Layout.PreflightItems).ToHaveCountAsync(3);
        await Expect(Layout.ExportPreflight).ToContainTextAsync("The Velocity Trap: copyright risk (quote over limit)");
        await Expect(Layout.ExportQueue).ToBeDisabledAsync();
        await Layout.ExportDraft.CheckAsync();
        await Expect(Layout.ExportQueue).ToBeEnabledAsync();
        await Layout.ExportDraft.UncheckAsync();
        await Expect(Layout.ExportQueue).ToBeDisabledAsync();
    }

    [Test, Order(2)]
    public async Task Export_WhenAnImageIsMissing_ShouldBlockWarnAndNoteInPreflight()
    {
        await OpenExportAsync(Guide);

        await Expect(Layout.ExportVerdict).ToHaveTextAsync("1 issue blocks export");
        await Expect(Layout.ExportPreflight).ToContainTextAsync("Image missing");
        await Expect(Layout.ExportPreflight).ToContainTextAsync("fallen-world-map.png is 107 dpi");
        await Expect(Layout.ExportPreflight).ToContainTextAsync("Raw LaTeX kept by this template");
        await Expect(Layout.ExportCrop).ToBeDisabledAsync();
        await Layout.ExportBleed("3mm").ClickAsync();
        await Expect(Layout.ExportCrop).ToBeEnabledAsync();
    }

    [Test, Order(3)]
    public async Task Export_WhenADraftIsQueued_ShouldRunInTheBackgroundAndSurviveAReload()
    {
        await OpenExportAsync(Guide);
        await Layout.ExportBleed("3mm").ClickAsync();
        await Layout.ExportCrop.CheckAsync();
        await Layout.ExportQuality("screen").ClickAsync();
        await Layout.ExportDraft.CheckAsync();

        await Layout.ExportQueue.ClickAsync();

        await Expect(Layout.ExportsList).ToBeVisibleAsync();
        await Expect(Layout.ExportItems.Or(Layout.ExportRunning)).ToHaveCountAsync(1);
        await OpenExportsTabAsync();
        await Expect(Layout.ExportItems.Or(Layout.ExportRunning)).ToHaveCountAsync(1);
        await Expect(Layout.ExportDone).ToBeVisibleAsync(Build);
    }

    [Test, Order(4)]
    public async Task Export_WhenItFinished_ShouldBeInTheHistoryWithItsSettingsAndDownloadAsAPdf()
    {
        await OpenExportsTabAsync();

        var item = Layout.ExportItems.Filter(new() { Has = Layout.ExportDone });
        await Expect(item).ToHaveCountAsync(1);
        await Expect(item).ToContainTextAsync("novel-6x9 v1.0.0 · 6x9 · 3 mm bleed · crop marks · 150 dpi · DRAFT");
        await Expect(Layout.ExportDone).ToHaveTextAsync(new Regex(@"^Done · [1-9]\d* pages · "));
        await Expect(Layout.ExportDownload).ToHaveAttributeAsync("href", new Regex(@"^/api/exports/[0-9a-f-]+/file$"));
        var pdf = await DownloadAsync((await Layout.ExportDownload.GetAttributeAsync("href"))!);
        pdf.Take(5).ShouldBe("%PDF-"u8.ToArray());
    }

    [Test, Order(5)]
    public async Task Export_WhenExportedAgainWithTheSameSettings_ShouldOpenTheDialogWithThem()
    {
        await OpenExportsTabAsync();

        await Layout.ExportAgain.ClickAsync();

        await Expect(Layout.ExportDialog).ToBeVisibleAsync();
        await Expect(Layout.ExportBleed("3mm")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Layout.ExportQuality("screen")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(Layout.ExportDraft).ToBeCheckedAsync();
        await Layout.ExportCancelDialog.ClickAsync();
        await Expect(Layout.ExportDialog).ToBeHiddenAsync();
    }

    [Test, Order(6)]
    public async Task Source_WhenQueuedFromSettings_ShouldBecomeAZipThatDownloads()
    {
        await Page.GotoAsync(Links.Settings(Guide, Links.Hash(("archive", null))));
        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Guide}/export-source", "POST", () => new SettingsScreen(Page).ExportSource.ClickAsync());
        response.Status.ShouldBe(200);

        await OpenExportsTabAsync();
        var done = Layout.SourceItems.Filter(new() { Has = Layout.ExportDone });
        await Expect(done).ToHaveCountAsync(1, new() { Timeout = 60_000 });
        var href = await done.GetByTestId(LayoutTestIds.ExportDownload).GetAttributeAsync("href");
        var zip = await DownloadAsync(href!);
        zip.Take(2).ShouldBe("PK"u8.ToArray());
    }

    [Test, Order(7)]
    public async Task Settings_WhenATrimIsPressed_ShouldChangeTheTrimLayoutUses()
    {
        await Page.GotoAsync(Links.Settings(Guide, Links.Hash(("layout", null))));
        var settings = new SettingsScreen(Page);
        await Expect(settings.LayoutTemplatePick).ToHaveValueAsync("novel-6x9");

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{Guide}/layout", "PUT", () => settings.LayoutTrim("5.5x8.5").ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(settings.LayoutTrim("5.5x8.5")).ToHaveAttributeAsync("aria-current", "true");
        await Page.GotoAsync(Links.Layout(Guide));
        await Expect(Layout.TrimSelect).ToHaveValueAsync("5.5x8.5");
        await Expect(Layout.StatusPages).ToHaveTextAsync(Pages);
    }
}
