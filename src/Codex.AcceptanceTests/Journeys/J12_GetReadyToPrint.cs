using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.Layout;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Ai;
using Codex.Contracts.Storage;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Codex.AcceptanceTests.Journeys;

// J12 · Get ready to print (#16 Part 2): the three copyright risks on Product Engineering are worked down (Team
// Topologies: permission granted, as J10's second path; Ship It Friday: the epigraph is cut; The Velocity Trap: the
// Ch 8 quote is paraphrased, as J10's first path) until the strip reads "Ready for print export"; then the
// bibliography is checked in Chicago and moved to APA, the permissions log and CSL JSON are downloaded, Write's Format
// → Preview pane pages the prose in Novel 6 x 9 and opens it in Layout, and Export for printer (6x9, 0.125 in bleed,
// print quality) is queued. The preview and Layout steps need pandoc, Tectonic and codex-sandbox, as CI installs them.
// The blocked case (risks still open)
// is J12_GetReadyToPrint_RisksOpen.
[TestFixture, Category("J12")]
public class J12_GetReadyToPrint : JourneyTest
{
    private const string Rewrite = "Velocity became the number everyone watched and nobody understood. ";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid TeamTopologies = SeedIds.References.TeamTopologies;
    private static readonly Guid ShipItFriday = SeedIds.References.ShipItFriday;
    private static readonly Guid Velocity = SeedIds.References.VelocityTrap;
    private static readonly Guid Ch8Use = SeedIds.ReferenceUses.VelocityTrapCh8Sc1;
    private static readonly Guid Ch8Scene = SeedIds.Scenes.Ch8Sc1;
    private static readonly FakeProseGenerator Generator = new([Rewrite]);
    private static readonly byte[] Letter = "%PDF-1.4 signed letter"u8.ToArray();
    private static readonly LocatorAssertionsToBeVisibleOptions Build = new() { Timeout = 120_000 };

    private static IReadOnlyList<string> _chicago = [];

    protected override Action<IServiceCollection>? ConfigureTestServices => services =>
    {
        services.AddSingleton<IProseGenerator>(Generator);
        services.AddSingleton<IBlobStore>(new InMemoryBlobStore());
    };

    // The draft panel offers Generate only once the user has an AI integration (#36); the fake ignores its key.
    [OneTimeSetUp]
    public async Task SeedAiIntegrationAsync() => await AiIntegrationRows.InsertAsync(ConnectionString, Host.Services);

    private static string SourceUrl(Guid source) => Links.References(BookId, Links.Hash((source.ToString(), null)));

    private async Task<long> RisksAsync() =>
        await ScalarSqlAsync<long>("SELECT count(*) FROM reference_clearance WHERE book_id = @book AND status = 'risk'", ("book", SeedIds.Books.ProductEngineering));

    [Test, Order(1)]
    public async Task Step1_WhenProductEngineeringOpenedFromTheLibrary_ShouldShowPlan()
    {
        await Page.GotoAsync(Links.Library());

        await new LibraryShelf(Page).Tile(BookId).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/plan$"));
        await Expect(new PlanScreen(Page).Board).ToBeVisibleAsync();
    }

    [Test, Order(2)]
    public async Task Step2_WhenReferencesTabClicked_ShouldShowBadge3AndThreeRisksBlockPrintExport()
    {
        var bar = new AppShell(Page).BookBar;
        await Page.GotoAsync(Links.Plan(BookId));
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("3");

        await bar.TabReferences.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/references"));
        var screen = new ReferencesScreen(Page);
        await Expect(screen.Gate).ToHaveTextAsync("3 risks block print export");
        await Expect(screen.Gate).ToHaveAttributeAsync("data-blocked", "true");
        await Expect(screen.ClearedText).ToHaveTextAsync("4 of 11 cited sources cleared");
    }

    [Test, Order(3)]
    public async Task Step3_WhenTeamTopologiesPermissionIsGranted_ShouldLiftItsRiskAndLeaveTwo()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceUrl(TeamTopologies));
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await Expect(panel.VerdictReason).ToHaveTextAsync("Permission asked, no reply");
        await panel.PermGranted.ClickAsync();
        await panel.PermFile.SetInputFilesAsync(new FilePayload { Name = "team-topologies.pdf", MimeType = "application/pdf", Buffer = Letter });
        await panel.PermCreditInput.FillAsync("Figure used by permission of IT Revolution.");

        var response = await Page.RunAndWaitForApiAsync($"/api/permission-requests/{SeedIds.PermissionRequests.TeamTopologies}/granted", "POST", () => panel.PermGrantSubmit.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(panel.PermStatus).ToHaveTextAsync("Permission granted");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("2");
        (await RisksAsync()).ShouldBe(2);
    }

    [Test, Order(4)]
    public async Task Step4_WhenTheShipItFridayEpigraphIsCut_ShouldMakeItNotCitedAndLeaveOneRisk()
    {
        var write = new WritePageObject(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await write.Marker(SeedIds.ReferenceUses.ShipItFridayCh10Sc1).ClickAsync();
        await Expect(write.MarkerCardSource).ToContainTextAsync("Ship It Friday");
        await Expect(write.MarkerCardStatus).ToHaveTextAsync("Copyright risk");
        var use = SeedIds.ReferenceUses.ShipItFridayCh10Sc1;

        var response = await Page.RunAndWaitForApiAsync($"/api/citations/{use}/remove", "POST", () => new CitePage(Page).Remove.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(write.Marker(use)).ToHaveCountAsync(0);
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveTextAsync("1");
        (await RisksAsync()).ShouldBe(1);
        (await ScalarSqlAsync<string>("SELECT status FROM reference_clearance WHERE reference_id = @id", ("id", ShipItFriday))).ShouldBe("not_cited");
    }

    [Test, Order(5)]
    public async Task Step5_WhenTheVelocityTrapQuoteIsParaphrased_ShouldClearTheLastRiskAndOpenPrintExport()
    {
        var write = new WritePageObject(Page);
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceUrl(Velocity));
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");
        await new CiteHandOffPage(Page).Paraphrase(Ch8Use).ClickAsync();
        await Expect(write.BeatCards(Ch8Scene)).ToHaveCountAsync(2);
        var beatId = await ScalarSqlAsync<Guid>(
            "SELECT id FROM beat WHERE scene_id = @scene AND body LIKE 'Paraphrase the passage quoted from The Velocity Trap%'", ("scene", Ch8Scene));
        (await Page.RunAndWaitForApiAsync($"/api/beats/{beatId}/generate", "POST", () => write.BeatGenerate(beatId).ClickAsync())).Ok.ShouldBeTrue();
        await Expect(write.Draft(beatId)).ToHaveAttributeAsync("data-state", "pending");
        var generationId = await write.Draft(beatId).GetAttributeAsync("data-generation-id");
        (await Page.RunAndWaitForApiAsync($"/api/generations/{generationId}/apply", "POST", () => write.DraftApply(beatId).ClickAsync())).Ok.ShouldBeTrue();
        await Expect(write.ProseEditor(Ch8Scene)).ToContainTextAsync(Rewrite.Trim());
        await Page.GotoAsync(SourceUrl(Velocity));

        var patched = await Page.RunAndWaitForApiAsync($"/api/reference-uses/{Ch8Use}", "PATCH", () => panel.UseKind(Ch8Use).SelectOptionAsync("paraphrase"));

        patched.Ok.ShouldBeTrue();
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        (await RisksAsync()).ShouldBe(0);
        // Deviation: the strip does not read "No risks block print export"; with no risks it reads "Ready for print export".
        await Page.GotoAsync(Links.References(BookId));
        var screen = new ReferencesScreen(Page);
        await Expect(screen.Gate).ToHaveTextAsync("Ready for print export");
        await Expect(screen.Gate).ToHaveAttributeAsync("data-blocked", "false");
        // The danger badge is gone: what is left on it is the warn count of sources still to check, and Layout's blocker chip is hidden.
        await Expect(new AppShell(Page).BookBar.ReferencesBadge).ToHaveAttributeAsync("data-tone", "warn");
        await Expect(Page.GetByTestId(ShellTestIds.LayoutBadge)).ToHaveCountAsync(0);
    }

    [Test, Order(6)]
    public async Task Step6_WhenBibliographyViewOpened_ShouldPreviewTenCitedSourcesInChicago()
    {
        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(Links.References(BookId));

        await screen.ViewBibliography.ClickAsync();

        await Expect(Page.GetByTestId(ReferencesTestIds.BibStyle)).ToHaveValueAsync("chicago");
        // 11 cited sources at the start; Ship It Friday dropped out with its epigraph, so it joins uncited Lean Software Development.
        await Expect(screen.BibliographyEntries).ToHaveCountAsync(10);
        await Expect(screen.BibliographyEntries.Filter(new() { HasText = "Lean Software Development" })).ToHaveCountAsync(0);
        await Expect(screen.BibliographyEntries.Filter(new() { HasText = "Ship It Friday" })).ToHaveCountAsync(0);
        await Expect(screen.BibliographyEntries.Filter(new() { HasText = "The Velocity Trap" })).ToHaveCountAsync(1);
        await Expect(screen.BibliographyCredits.Filter(new() { HasText = "Figure used by permission of IT Revolution." })).ToBeVisibleAsync();
        _chicago = await screen.BibliographyEntries.AllInnerTextsAsync();
    }

    [Test, Order(7)]
    public async Task Step7_WhenCitationStyleChangedToApaInBookSettings_ShouldReformatEveryEntry()
    {
        var settings = new SettingsScreen(Page);
        await Page.GotoAsync(Links.Settings(BookId, Links.Hash(("citations", null))));
        await Expect(settings.CitationStyleOption("chicago")).ToHaveAttributeAsync("aria-pressed", "true");

        var saved = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/publication-settings", "PATCH", () => settings.CitationStyleOption("apa").ClickAsync());

        saved.Ok.ShouldBeTrue();
        await Expect(settings.CitationStyleOption("apa")).ToHaveAttributeAsync("aria-pressed", "true");
        (await ScalarSqlAsync<string>("SELECT citation_style FROM publication_settings WHERE book_id = @id", ("id", SeedIds.Books.ProductEngineering))).ShouldBe("apa");

        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(Links.References(BookId));
        await screen.ViewBibliography.ClickAsync();
        await Expect(Page.GetByTestId(ReferencesTestIds.BibStyle)).ToHaveValueAsync("apa");
        await Expect(screen.BibliographyEntries).ToHaveCountAsync(10);
        (await screen.BibliographyEntries.AllInnerTextsAsync()).ShouldNotBe(_chicago);
    }

    [Test, Order(8)]
    public async Task Step8_WhenPermissionsLogAndCslJsonExported_ShouldDownloadBothForThePublisher()
    {
        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(Links.References(BookId));

        var log = await screen.ExportAsync("permissions");

        log.SuggestedFilename.ShouldEndWith(".csv");
        using (var reader = new StreamReader(await log.CreateReadStreamAsync()))
        {
            var csv = await reader.ReadToEndAsync();
            csv.ShouldContain("Source,Authors,Year");
            csv.ShouldContain("Team Topologies");
            csv.ShouldContain("Figure used by permission of IT Revolution.");
        }

        await Page.GotoAsync(Links.References(BookId));
        var csl = await screen.ExportAsync("csljson");

        csl.SuggestedFilename.ShouldEndWith(".json");
        using var json = new StreamReader(await csl.CreateReadStreamAsync());
        (await json.ReadToEndAsync()).ShouldContain("\"title\": \"The Velocity Trap\"");
    }

    [Test, Order(9)]
    public async Task Step9_WhenFormatPreviewShownAndNovelTemplatePicked_ShouldPageTheProseInNovel6x9()
    {
        var write = new WritePageObject(Page);
        var layout = new LayoutScreen(Page);
        await Page.GotoAsync(Links.Write(BookId));
        await Expect(write.Manuscript).ToBeVisibleAsync();

        await write.ShowPreviewAsync();
        await Expect(write.Preview).ToBeVisibleAsync();
        var saved = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/layout", "PUT", () => write.PreviewTemplate.SelectOptionAsync("novel-6x9"));

        saved.Ok.ShouldBeTrue();
        await Expect(write.PreviewTemplate).ToHaveValueAsync("novel-6x9");
        await Expect(write.PreviewPages).ToHaveTextAsync(new Regex(@"^≈ [1-9]\d* pages?$"), new() { Timeout = 120_000 });
        await write.PreviewInLayout.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/books/{BookId}/layout"));
        await Expect(layout.TemplateSelect).ToHaveValueAsync("novel-6x9");
        await Expect(layout.TrimSelect).ToHaveValueAsync("6x9");
        await Expect(layout.StatusPages).ToHaveTextAsync(new Regex(@"^[1-9]\d* pages?$"), new() { Timeout = 120_000 });
    }

    [Test, Order(10)]
    public async Task Step10_WhenExportForPrinterQueued_ShouldPassPreflightAndRunTheJob()
    {
        var layout = new LayoutScreen(Page);
        await Page.GotoAsync(Links.Layout(BookId));
        await layout.ExportOpen.ClickAsync();
        await Expect(layout.ExportDialog).ToBeVisibleAsync();
        await layout.ExportBleed("0.125in").ClickAsync();
        await layout.ExportQuality("print").ClickAsync();

        await Expect(layout.ExportVerdict).ToHaveTextAsync("Ready to export");
        await Expect(layout.ExportDraft).ToHaveCountAsync(0);
        await Expect(layout.ExportQueue).ToBeEnabledAsync();
        var queued = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/exports", "POST", () => layout.ExportQueue.ClickAsync());

        queued.Status.ShouldBe(202);
        await Expect(layout.ExportsList).ToBeVisibleAsync();
        await Expect(layout.ExportItems.Or(layout.ExportRunning)).ToHaveCountAsync(1);
        (await ScalarSqlAsync<string>(
            "SELECT trim_size || ' ' || (settings ->> 'bleed') || ' ' || (settings ->> 'quality') FROM export_job WHERE book_id = @id AND kind = 'print'",
            ("id", SeedIds.Books.ProductEngineering))).ShouldBe("6x9 0.125in print");
        // Deviation: there is no completion notification beyond the job's own row going to Done in the Exports list.
        await Expect(layout.ExportDone).ToBeVisibleAsync(Build);
    }
}
