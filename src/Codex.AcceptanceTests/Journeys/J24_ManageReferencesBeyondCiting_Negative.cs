using System.Text;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.References;
using Codex.Contracts;
using Codex.Contracts.Sources;
using Codex.TestSupport.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.AcceptanceTests.Journeys;

// J24 negative path (docs/journeys-wave5.md): each test says what must not be written. They run on one fresh clone and
// leave the data as they found it, except where a test says otherwise.
[TestFixture, Category("J24")]
public class J24_ManageReferencesBeyondCiting_Negative : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();

    protected override Action<IServiceCollection>? ConfigureTestServices => services => services.AddSingleton<ISourceLookup>(new FakeSourceLookup());

    private static string SourceLink(Guid sourceId) => Links.References(BookId, Links.Hash((sourceId.ToString(), null)));

    private async Task<long> LiveSourcesAsync() =>
        await ScalarSqlAsync<long>("SELECT count(*) FROM reference WHERE book_id = @book AND archived_at IS NULL", ("book", SeedIds.Books.ProductEngineering));

    [Test, Order(1)]
    public async Task Import_WhenNoFileIsChosen_ShouldKeepImportDisabled()
    {
        var form = new AddSourceForm(Page);
        await Page.GotoAsync(Links.References(BookId));

        await form.ImportOpen.ClickAsync();

        await Expect(form.ImportSubmit).ToBeDisabledAsync();
        (await LiveSourcesAsync()).ShouldBe(12);
    }

    [Test, Order(2)]
    public async Task Import_WhenTheFileIsNotCslBibtexOrRis_ShouldSayWhatCouldNotBeReadAndWriteNothing()
    {
        var form = new AddSourceForm(Page);
        await Page.GotoAsync(Links.References(BookId));
        await form.ImportOpen.ClickAsync();

        await form.ImportFile.SetInputFilesAsync(new FilePayload { Name = "library.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes("{ this is not json") });

        await Expect(form.ImportProblems).ToContainTextAsync("not valid JSON");
        await Expect(form.ImportSubmit).ToBeDisabledAsync();
        (await LiveSourcesAsync()).ShouldBe(12);
    }

    [Test, Order(3)]
    public async Task Lookup_WhenTheIsbnIsAlreadyInReferences_ShouldOfferTheExistingSourceAndWriteNothing()
    {
        var form = new AddSourceForm(Page);
        await Page.GotoAsync(Links.References(BookId, Links.Hash(("new", null))));
        await form.Input.FillAsync("978-1-7364260-0-5");
        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references", "POST", () => form.Lookup.ClickAsync());
        (await LiveSourcesAsync()).ShouldBe(13);

        await Page.GotoAsync(Links.Library());
        await Page.GotoAsync(Links.References(BookId, Links.Hash(("new", null))));
        await Expect(form.Form).ToBeVisibleAsync();
        await form.Input.FillAsync("978-1-7364260-0-5");
        await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/references/lookup", "POST", () => form.Lookup.ClickAsync());

        await Expect(form.UseExisting).ToContainTextAsync("Use existing source: Shape Up");
        (await LiveSourcesAsync()).ShouldBe(13);
    }

    [Test, Order(4)]
    public async Task Edit_WhenTheYearIsText_ShouldShowAnInlineErrorAndSaveNothing()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(SeedIds.References.ConwayCommittees));
        await Expect(panel.Field("year")).ToHaveValueAsync("1968");

        await panel.Field("year").FillAsync("nineteen sixty-seven");
        await panel.Field("year").BlurAsync();

        await Expect(panel.Banner).ToHaveTextAsync("The year must be a number.");
        (await ScalarSqlAsync<int>("SELECT year FROM reference WHERE id = @id", ("id", SeedIds.References.ConwayCommittees))).ShouldBe(1968);
        await Expect(panel.VerdictStatus).ToHaveTextAsync("Cleared");
    }

    [Test, Order(5)]
    public async Task Merge_WhenOpenedOnASource_ShouldNotOfferTheSourceItself()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(SeedIds.References.ConwayCommittees));

        await panel.Menu.ClickAsync();
        await panel.MenuMerge.ClickAsync();

        (await panel.MergeTarget.InnerTextAsync()).ShouldNotContain("How Do Committees Invent?");
        (await panel.MergeTarget.InnerTextAsync()).ShouldContain("Accelerate");
    }

    [Test, Order(6)]
    public async Task Merge_WhenBothSourcesHaveALivePermissionRequest_ShouldBeBlockedAndChangeNothing()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(SeedIds.References.ShipItFriday));
        await Page.RunAndWaitForApiAsync($"/api/references/{SeedIds.References.ShipItFriday}/permission", "POST", () => panel.PermDraft.ClickAsync());
        await panel.Menu.ClickAsync();
        await panel.MenuMerge.ClickAsync();
        await panel.MergeTarget.SelectOptionAsync(SeedIds.References.TeamTopologies.ToString());

        var response = await Page.RunAndWaitForApiAsync($"/api/references/{SeedIds.References.ShipItFriday}/merge", "POST", () => panel.MergeConfirm.ClickAsync());

        response.Status.ShouldBe(422);
        await Expect(panel.Banner).ToHaveTextAsync("Both have a permission request. Withdraw one first.");
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NULL AND merged_into_id IS NULL FROM reference WHERE id = @id", ("id", SeedIds.References.ShipItFriday))).ShouldBeTrue();
    }

    [Test, Order(7)]
    public async Task Remove_WhenTheDialogIsCancelled_ShouldKeepTheSourceAndItsMarkers()
    {
        var panel = new SourcePanel(Page);
        await Page.GotoAsync(SourceLink(SeedIds.References.VelocityTrap));
        await panel.Menu.ClickAsync();
        await panel.MenuRemove.ClickAsync();
        await Expect(panel.RemoveConfirm).ToBeVisibleAsync();

        await panel.RemoveCancel.ClickAsync();

        await Expect(panel.RemoveConfirm).ToHaveCountAsync(0);
        await Expect(panel.Title).ToHaveTextAsync("The Velocity Trap");
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NULL FROM reference WHERE id = @id", ("id", SeedIds.References.VelocityTrap))).ShouldBeTrue();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM reference_use WHERE reference_id = @id AND archived_at IS NULL", ("id", SeedIds.References.VelocityTrap))).ShouldBe(2);
    }
}
