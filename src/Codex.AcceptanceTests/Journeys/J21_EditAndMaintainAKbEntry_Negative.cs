using System.Net;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.AcceptanceTests.Pages.Plan;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J21 negative path, from a fresh seed: an empty or taken name, an entry named like an alias, a category and a label
// that already exist, and cancelling a delete or a merge.
[TestFixture, Category("J21")]
public class J21_EditAndMaintainAKbEntry_Negative : JourneyTest
{
    private static readonly string BookId = SeedIds.Books.ProductEngineering.ToString();
    private static readonly Guid Goodhart = SeedIds.KbEntries.GoodhartsLaw;

    private KbEntryWindow Window => new(Page);

    private static string EntryUrl(Guid entryId) => Links.Plan(BookId, Links.Hash(("kb", entryId.ToString()), ("entry", entryId.ToString())));

    [Test, Order(1)]
    public async Task Rename_WhenEmpty_ShouldShowTheErrorAndKeepTheName()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Window.Name.FillAsync("   ");
        await Window.Name.PressAsync("Enter");

        await Expect(Window.EntryError).ToBeVisibleAsync();
        await Expect(Window.Name).ToHaveValueAsync("Goodhart's Law");
        (await ScalarSqlAsync<string>("SELECT name FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBe("Goodhart's Law");
    }

    [Test, Order(2)]
    public async Task Rename_WhenAnotherEntryHasTheName_ShouldBlockUntilRenameAnyway()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Window.Name.FillAsync("Conway's Law");
        await Window.Name.PressAsync("Enter");

        await Expect(Window.EntryError).ToContainTextAsync("already a name or alias");
        (await ScalarSqlAsync<string>("SELECT name FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBe("Goodhart's Law");
        var renamed = await Page.RunAndWaitForResponseAsync(() => Window.RenameAnyway.ClickAsync(),
            r => r.Request.Method == "PATCH" && r.Request.PostData!.Contains("allowDuplicate\":true", StringComparison.Ordinal));
        renamed.Status.ShouldBe((int)HttpStatusCode.OK);
        (await ScalarSqlAsync<string>("SELECT name FROM kb_entry WHERE id = @id", ("id", Goodhart))).ShouldBe("Conway's Law");
    }

    [Test, Order(3)]
    public async Task Create_WhenNamedLikeAnAlias_ShouldExplainAndWriteNothingUntilConfirmed()
    {
        await Page.GotoAsync(Links.Plan(BookId));
        await Window.NewEntryButton(SeedIds.KbCategories.Research).ClickAsync();
        await Window.DraftName.FillAsync("DORA");
        await Window.DraftName.PressAsync("Enter");

        await Expect(Window.DraftError).ToContainTextAsync("DORA Four Keys");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_entry WHERE name = 'DORA'")).ShouldBe(0);
        var created = await Page.RunAndWaitForApiAsync($"/api/books/{BookId}/kb/entries", "POST", () => Window.DraftCreateAnyway.ClickAsync());
        created.Status.ShouldBe((int)HttpStatusCode.Created);
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_entry WHERE name = 'DORA'")).ShouldBe(1);
    }

    [Test, Order(4)]
    public async Task NewCategory_WhenNamedLaws_ShouldShowTheErrorAndAddNothing()
    {
        await Page.GotoAsync(EntryUrl(Goodhart));
        await Window.CategoryChip.ClickAsync();
        await Window.NewCategoryOpen.ClickAsync();
        await Window.NewCategoryName.FillAsync("Laws");
        await Window.NewCategoryCreate.ClickAsync();

        await Expect(Window.NewCategoryError).ToBeVisibleAsync();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM kb_category WHERE name = 'Laws' AND book_id = @book", ("book", SeedIds.Books.ProductEngineering))).ShouldBe(1);
    }

    [Test, Order(5)]
    public async Task NewLabel_WhenNamedMaybeCut_ShouldShowTheDuplicateError()
    {
        var plan = new PlanScreen(Page);
        await Page.GotoAsync(Links.Plan(BookId));
        await plan.LabelFilter.ClickAsync();
        await plan.ManageLabels.ClickAsync();
        await plan.LabelNewName.FillAsync("Maybe cut");
        await plan.LabelNewCreate.ClickAsync();

        await Expect(plan.LabelError).ToContainTextAsync("already exists");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM label WHERE name = 'Maybe cut'")).ShouldBe(1);
    }

    [Test, Order(6)]
    public async Task Delete_WhenCancelled_ShouldChangeNothing()
    {
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.BrooksLaw));
        await Window.Menu.ClickAsync();
        await Window.MenuDelete.ClickAsync();
        await Expect(Window.DeleteDialog).ToBeVisibleAsync();
        await Window.DeleteCancel.ClickAsync();

        await Expect(Window.DeleteDialog).ToHaveCountAsync(0);
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NULL FROM kb_entry WHERE id = @id", ("id", SeedIds.KbEntries.BrooksLaw))).ShouldBeTrue();
    }

    [Test, Order(7)]
    public async Task Merge_WhenCancelled_ShouldMoveNothingAndNeverOfferTheEntryItself()
    {
        await Page.GotoAsync(EntryUrl(SeedIds.KbEntries.BrooksLaw));
        await Window.Menu.ClickAsync();
        await Window.MenuMerge.ClickAsync();
        await Expect(Window.MergeOption(SeedIds.KbEntries.BrooksLaw)).ToHaveCountAsync(0);
        await Expect(Window.MergeOption(SeedIds.KbEntries.ConwaysLaw)).ToBeVisibleAsync();
        await Window.MergeOption(SeedIds.KbEntries.ConwaysLaw).ClickAsync();
        await Window.MergeCancel.ClickAsync();

        await Expect(Window.MergeDialog).ToHaveCountAsync(0);
        (await ScalarSqlAsync<bool>("SELECT archived_at IS NULL FROM kb_entry WHERE id = @id", ("id", SeedIds.KbEntries.BrooksLaw))).ShouldBeTrue();
    }
}
