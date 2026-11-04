using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Settings;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Journeys;

// J26 · Publish lifecycle, negative path: cancelled dialogs, a book with no chapter, an empty title.
[TestFixture, Category("J26")]
public class J26_PublishLifecycle_Negative : JourneyTest
{
    private static readonly string Undertow = SeedIds.Books.Undertow.ToString();
    private static readonly string Northbound = SeedIds.Books.Northbound.ToString();
    private static readonly string Product = SeedIds.Books.ProductEngineering.ToString();

    private SettingsScreen Settings => new(Page);

    [Test, Order(1)]
    public async Task CancelPublishedDialog_ShouldChangeNothing()
    {
        await Page.GotoAsync(Links.Settings(Undertow));
        await Settings.StatusOption("published").ClickAsync();
        await Expect(Settings.PublishDialog).ToBeVisibleAsync();

        await Settings.PublishCancel.ClickAsync();

        await Expect(Settings.PublishDialog).ToBeHiddenAsync();
        await Expect(Settings.StatusOption("writing")).ToHaveAttributeAsync("aria-pressed", "true");
        (await ScalarSqlAsync<string>("SELECT status FROM book WHERE id = @id::uuid", ("id", Undertow))).ShouldBe("writing");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM export_job WHERE book_id = @id::uuid", ("id", Undertow))).ShouldBe(0);
    }

    [Test, Order(2)]
    public async Task BookWithNoChapter_ShouldNotBePublishable()
    {
        await Page.GotoAsync(Links.Settings(Northbound));

        await Settings.StatusOption("published").ClickAsync();

        await Expect(Settings.PublishMessage).ToHaveTextAsync("Nothing to publish. Add a chapter first.");
        await Expect(Settings.PublishConfirm).ToBeDisabledAsync();
        await Settings.PublishCancel.ClickAsync();
        (await ScalarSqlAsync<string>("SELECT status FROM book WHERE id = @id::uuid", ("id", Northbound))).ShouldBe("planning");
        (await ScalarSqlAsync<long>("SELECT count(*) FROM export_job WHERE book_id = @id::uuid", ("id", Northbound))).ShouldBe(0);
    }

    [Test, Order(3)]
    public async Task NewEditionWithEmptyTitle_ShouldNotCreateABook()
    {
        var before = await ScalarSqlAsync<long>("SELECT count(*) FROM book");
        await Page.GotoAsync(Links.Settings(Product, Links.Hash(("archive", null))));
        await Settings.NewEdition.ClickAsync();

        await Settings.DuplicateTitle.FillAsync("   ");

        await Expect(Settings.DuplicateConfirm).ToBeDisabledAsync();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book")).ShouldBe(before);
    }

    [Test, Order(4)]
    public async Task CancelDuplicate_ShouldNotCreateABook()
    {
        var before = await ScalarSqlAsync<long>("SELECT count(*) FROM book");
        await Page.GotoAsync(Links.Settings(Product, Links.Hash(("archive", null))));
        await Settings.Duplicate.ClickAsync();

        await Settings.DuplicateCancel.ClickAsync();

        await Expect(Settings.DuplicateDialog).ToBeHiddenAsync();
        (await ScalarSqlAsync<long>("SELECT count(*) FROM book")).ShouldBe(before);
    }
}
