using System.Text;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Cite;
using Codex.AcceptanceTests.Pages.References;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.AcceptanceTests.Pages.Write;
using Codex.Contracts;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J24 · Manage references beyond citing (docs/journeys-wave5.md). Import a CSL JSON library (previewed first), look up
// a DOI, fix a cleared source (which reopens it), merge a hand-typed duplicate, retype a use, un-cite a marker in
// Write, and delete a source that still has uses (rule R4: its markers become plain text, a revision is cut).
[TestFixture, Category("J24")]
public class J24_ManageReferencesBeyondCiting : JourneyTest
{
    private const string AccelerateIsbn = "9781942788331";

    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly string Library = """
        [
          { "id": "fowler2018", "type": "book", "title": "Refactoring", "author": [{ "family": "Fowler", "given": "Martin" }],
            "issued": { "date-parts": [[2018]] }, "publisher": "Addison-Wesley", "ISBN": "978-0-13-475759-9" },
          { "id": "kim2013", "type": "book", "title": "The Phoenix Project", "author": [{ "family": "Kim", "given": "Gene" }],
            "issued": { "date-parts": [[2013]] }, "publisher": "IT Revolution" },
          { "id": "accelerate", "type": "book", "title": "Accelerate", "author": [{ "family": "Forsgren", "given": "Nicole" }],
            "issued": { "date-parts": [[2018]] }, "ISBN": "978-1-942788-33-1" },
          { "id": "untitled", "type": "book", "author": [{ "family": "Nobody" }] }
        ]
        """;

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    private static string NewLink => Links.References(BookId, Links.Hash(("new", null)));

    private static string SourceLink(Guid sourceId) => Links.References(BookId, Links.Hash((sourceId.ToString(), null)));

    private static FilePayload Payload(string name, string text) => new() { Name = name, MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(text) };

    [Test, Order(1)]
    public async Task Step1_WhenAFourItemCslFileIsChosen_ShouldSummariseWhatImportWouldDoAndWriteNothing()
    {
        // The seed's Accelerate has no ISBN; the file's copy has, so record it for the import to match on.
        await ScalarSqlAsync<int>("UPDATE reference SET isbn = @isbn WHERE id = @id RETURNING 1", ("isbn", AccelerateIsbn), ("id", SeedIds.References.Accelerate));
        var screen = new ReferencesScreen(Page);
        var form = new AddSourceForm(Page);
        await Page.GotoAsync(Links.References(BookId));
        await Expect(screen.Rows).ToHaveCountAsync(12);

        await form.ImportOpen.ClickAsync();
        await form.ImportFile.SetInputFilesAsync(Payload("library.json", Library));

        await Expect(form.ImportSummary).ToHaveTextAsync("2 new · 1 already in References · 1 skipped (no title)");
        await Expect(form.ImportSkipped).ToContainTextAsync("Accelerate: already in References");
        await Expect(form.ImportSkipped).ToContainTextAsync("(untitled entry): no title");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE external_provider IS NOT NULL")).ShouldBe(0);
        await Expect(screen.Rows).ToHaveCountAsync(12);
    }

    [Test, Order(2)]
    public async Task Step2_WhenImported_ShouldAddTwoNotCitedSourcesWithZoteroKeysAndImportEvents()
    {
        var screen = new ReferencesScreen(Page);
        var form = new AddSourceForm(Page);
        await Page.GotoAsync(Links.References(BookId));
        await form.ImportOpen.ClickAsync();
        await form.ImportFile.SetInputFilesAsync(Payload("library.json", Library));
        await Expect(form.ImportSummary).ToHaveTextAsync("2 new · 1 already in References · 1 skipped (no title)");

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references/import", "POST", () => form.ImportSubmit.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(form.ImportResult).ToHaveTextAsync("2 added");
        await Expect(screen.Rows).ToHaveCountAsync(14);
        await Expect(screen.ChipCount("not_cited")).ToHaveTextAsync("3");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE external_provider = 'zotero'")).ShouldBe(2);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM reference_event e JOIN reference r ON r.id = e.reference_id WHERE r.external_provider = 'zotero' AND e.action = 'added' AND e.source = 'import'")).ShouldBe(2);
    }

    [Test, Order(3)]
    public async Task Step3_WhenADoiIsLookedUp_ShouldAddTheFixtureSourceAndAnUnknownDoiShouldOfferManualEntry()
    {
        var form = new AddSourceForm(Page);
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(NewLink);
        await form.Input.FillAsync("10.1000/xyz123");

        var response = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references", "POST", () => form.Lookup.ClickAsync());

        response.Status.ShouldBe(201);
        await Expect(panel.Title).ToHaveTextAsync("Sleep and Memory Consolidation");
        await Expect(panel.Field("doi")).ToHaveValueAsync("10.1000/xyz123");
        await Expect(new ReferencesScreen(Page).Rows).ToHaveCountAsync(15);

        await Page.GotoAsync(NewLink);
        await form.Input.FillAsync("10.9999/nobody-knows");
        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references/lookup", "POST", () => form.Lookup.ClickAsync());
        await Expect(form.NotFound).ToBeVisibleAsync();
        await Expect(form.Field("doi")).ToHaveValueAsync("10.9999/nobody-knows");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE book_id = @book AND archived_at IS NULL", ("book", SeedIds.Books.ProductEngineering))).ShouldBe(15);
    }

    [Test, Order(4)]
    public async Task Step4_WhenTheYearOfAClearedSourceChanges_ShouldReopenItAndDropClearedToThree()
    {
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(SourceLink(SeedIds.References.ConwayCommittees));
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
        await Expect(screen.ClearedText).ToHaveTextAsync("4 of 11 cited sources cleared");

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{SeedIds.References.ConwayCommittees}", "PATCH", async () =>
        {
            await panel.Field("year").FillAsync("1967");
            await panel.Field("year").BlurAsync();
        });

        response.Status.ShouldBe(200);
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(screen.Row(SeedIds.References.ConwayCommittees).GetByTestId(ReferencesTestIds.RowStatus)).ToHaveTextAsync("Needs check");
        await Expect(screen.ClearedText).ToHaveTextAsync("3 of 11 cited sources cleared");
        await Expect(panel.HistoryItems.First).ToContainTextAsync("reopened the source: details changed since sign-off");
        await Expect(panel.HistoryItems.Nth(1)).ToContainTextAsync("edited year");
    }

    [Test, Order(5)]
    public async Task Step5_WhenAHandTypedDuplicateIsMergedIntoAccelerate_ShouldArchiveItAndPointAtTheTarget()
    {
        var form = new AddSourceForm(Page);
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        await Page.GotoAsync(NewLink);
        await form.Manual.ClickAsync();
        await form.Field("title").FillAsync("Accelerate");
        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references", "POST", () => form.Save.ClickAsync());
        await Expect(panel.Title).ToHaveTextAsync("Accelerate");
        await Expect(screen.Rows).ToHaveCountAsync(16);

        await panel.Menu.ClickAsync();
        await panel.MenuMerge.ClickAsync();
        await Expect(panel.MergeSummary).ToHaveTextAsync("1 source's details and 0 uses move; the duplicate is archived.");
        await panel.MergeTarget.SelectOptionAsync(SeedIds.References.Accelerate.ToString());
        var response = await Page.RunAndWaitForApiAsync($"/api/references/{await DuplicateIdAsync()}/merge", "POST", () => panel.MergeConfirm.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.Root).ToHaveCountAsync(0);
        await Expect(screen.Rows).ToHaveCountAsync(15);
        (await ScalarSqlAsync<Guid>("SELECT merged_into_id FROM reference WHERE id = @id", ("id", await DuplicateIdAsync()))).ShouldBe(SeedIds.References.Accelerate);
    }

    [Test, Order(6)]
    public async Task Step6_WhenBecksParaphraseBecomesAQuoteAndBack_ShouldCountItsWordsThenDropThem()
    {
        var use = SeedIds.ReferenceUses.BeckXpCh3Sc1;
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(SeedIds.References.BeckXp));
        await Expect(panel.UseKind(use)).ToHaveValueAsync("paraphrase");
        (await ScalarSqlAsync<int>("SELECT quoted_words FROM reference_clearance WHERE reference_id = @id", ("id", SeedIds.References.BeckXp))).ShouldBe(0);

        await Page.RunAndWaitForApiAsync($"/api/reference-uses/{use}", "PATCH", () => panel.UseKind(use).SelectOptionAsync("quote"));

        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        await Expect(panel.VerdictReason).ToHaveTextAsync("New uses since cleared");
        var words = await ScalarSqlAsync<int>("SELECT word_count FROM reference_use WHERE id = @id", ("id", use));
        words.ShouldBeGreaterThan(0);
        (await ScalarSqlAsync<int>("SELECT quoted_words FROM reference_clearance WHERE reference_id = @id", ("id", SeedIds.References.BeckXp))).ShouldBe(words);

        await Page.RunAndWaitForApiAsync($"/api/reference-uses/{use}", "PATCH", () => panel.UseKind(use).SelectOptionAsync("paraphrase"));

        await Expect(panel.UseKind(use)).ToHaveValueAsync("paraphrase");
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Needs check");
        (await ScalarSqlAsync<int>("SELECT quoted_words FROM reference_clearance WHERE reference_id = @id", ("id", SeedIds.References.BeckXp))).ShouldBe(0);
    }

    [Test, Order(7)]
    public async Task Step7_WhenTheBrooksMarkerIsRemovedInWrite_ShouldKeepThePassageAndRenumberShipItFriday()
    {
        var write = new WritePageObject(Page);
        var cite = new CitePage(Page);
        var screen = new ReferencesScreen(Page);
        var brooks = SeedIds.ReferenceUses.MythicalManMonthCh9Sc1;
        await Page.GotoAsync(Links.Write(BookId, Links.Hash(("scene", "9.1"))));
        await Expect(write.ProseEditor(SeedIds.Scenes.Ch9Sc1)).ToBeVisibleAsync();
        await Expect(write.Marker(SeedIds.ReferenceUses.ShipItFridayCh10Sc1)).ToHaveAttributeAsync("data-number", "11");
        var excerpt = await ScalarSqlAsync<string>("SELECT excerpt FROM reference_use WHERE id = @id", ("id", brooks));
        await write.Marker(brooks).ClickAsync();
        await Expect(write.MarkerCardSource).ToContainTextAsync("The Mythical Man-Month");

        var response = await Page.RunAndWaitForApiAsync($"/api/citations/{brooks}/remove", "POST", () => cite.Remove.ClickAsync());

        response.Ok.ShouldBeTrue();
        await Expect(write.Marker(brooks)).ToHaveCountAsync(0);
        await Expect(write.Marker(SeedIds.ReferenceUses.ShipItFridayCh10Sc1)).ToHaveAttributeAsync("data-number", "10");
        (await ScalarSqlAsync<string>("SELECT body FROM prose WHERE scene_id = @id", ("id", SeedIds.Scenes.Ch9Sc1)))!.ShouldContain(excerpt!);
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NOT NULL FROM reference_use WHERE id = @id", ("id", brooks))).ShouldBeTrue();

        await Page.GotoAsync(SourceLink(SeedIds.References.MythicalManMonth));
        await Expect(new SourcePanel(Page).VerdictStatus).ToHaveTextAsync("Not cited");
        await Expect(screen.Row(SeedIds.References.MythicalManMonth).GetByTestId(ReferencesTestIds.RowNumber)).ToHaveTextAsync("–");
        await Expect(screen.ClearedText).ToContainTextAsync("of 10 cited sources cleared");
        await Expect(screen.Row(SeedIds.References.ShipItFriday).GetByTestId(ReferencesTestIds.RowNumber)).ToHaveTextAsync("10");
    }

    [Test, Order(8)]
    public async Task Step8_WhenTheVelocityTrapIsRemoved_ShouldTurnItsTwoMarkersIntoPlainTextAndCutARevisionPerScene()
    {
        var panel = new SourcePanel(Page);
        var screen = new ReferencesScreen(Page);
        var bar = new AppShell(Page).BookBar;
        var words4 = await WordsAsync(SeedIds.Scenes.Ch4Sc1);
        var words8 = await WordsAsync(SeedIds.Scenes.Ch8Sc1);
        var revisions4 = await RevisionsAsync(SeedIds.Scenes.Ch4Sc1);
        var revisions8 = await RevisionsAsync(SeedIds.Scenes.Ch8Sc1);
        words4.ShouldBe(231);
        words8.ShouldBe(162);
        await Page.GotoAsync(SourceLink(SeedIds.References.VelocityTrap));
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Copyright risk");

        await panel.Menu.ClickAsync();
        await panel.MenuRemove.ClickAsync();
        await Expect(panel.Root).ToContainTextAsync("Its 2 markers become plain text in Write.");
        var response = await Page.RunAndWaitForApiAsync($"/api/references/{SeedIds.References.VelocityTrap}/archive", "POST", () => panel.RemoveConfirm.ClickAsync());

        response.Status.ShouldBe(200);
        await Expect(panel.Root).ToHaveCountAsync(0);
        await Expect(bar.ReferencesBadge).ToHaveTextAsync("2");
        await Expect(screen.Gate).ToContainTextAsync("2 risks block print export");
        await Expect(screen.Row(SeedIds.References.VelocityTrap)).ToHaveCountAsync(0);
        (await WordsAsync(SeedIds.Scenes.Ch4Sc1)).ShouldBe(231);
        (await WordsAsync(SeedIds.Scenes.Ch8Sc1)).ShouldBe(162);
        (await RevisionsAsync(SeedIds.Scenes.Ch4Sc1)).ShouldBe(revisions4 + 1);
        (await RevisionsAsync(SeedIds.Scenes.Ch8Sc1)).ShouldBe(revisions8 + 1);
        (await ScalarSqlAsync<long>(
            "SELECT count(*) FROM prose WHERE body LIKE '%' || @a || '%' OR body LIKE '%' || @b || '%'",
            ("a", SeedIds.ReferenceUses.VelocityTrapCh4Sc1.ToString()), ("b", SeedIds.ReferenceUses.VelocityTrapCh8Sc1.ToString()))).ShouldBe(0);
        (await ScalarSqlAsync<bool>("SELECT reference_id IS NULL FROM quote_flag WHERE id = @id", ("id", SeedIds.QuoteFlags.Ch8Sc1VelocityTrapMatch))).ShouldBeTrue();
    }

    private async Task<Guid> DuplicateIdAsync() =>
        await ScalarSqlAsync<Guid>("SELECT id FROM reference WHERE title = 'Accelerate' AND id <> @id AND book_id = @book", ("id", SeedIds.References.Accelerate), ("book", SeedIds.Books.ProductEngineering));

    private async Task<int> WordsAsync(Guid sceneId) =>
        await ScalarSqlAsync<int>("SELECT word_count FROM prose WHERE scene_id = @id", ("id", sceneId));

    private async Task<long> RevisionsAsync(Guid sceneId) =>
        await ScalarSqlAsync<long>("SELECT count(*) FROM prose_revision WHERE scene_id = @id", ("id", sceneId));
}
