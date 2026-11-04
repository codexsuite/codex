using System.Text.RegularExpressions;
using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Kb;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Kb;

// The Knowledge base sidebar on Plan, from the seed. Steps run in order on one cloned database.
[TestFixture]
public class KbSidebarTests : SeededAcceptanceTest
{
    private KbSidebar Sidebar => new(Page);

    [Test, Order(1)]
    public async Task Sidebar_ShouldListSeedCategories()
    {
        await Page.GotoAsync(Links.Plan(SeedIds.Books.ProductEngineering.ToString()));

        await Expect(Sidebar.Item(SeedIds.KbEntries.GoodhartsLaw)).ToContainTextAsync("Goodhart's Law");
        foreach (var (category, name) in new[]
                 {
                     (SeedIds.KbCategories.BlogPosts, "Blog Posts"), (SeedIds.KbCategories.Laws, "Laws"),
                     (SeedIds.KbCategories.Stories, "Stories"), (SeedIds.KbCategories.Research, "Research"),
                     (SeedIds.KbCategories.People, "People"),
                 })
        {
            await Expect(Sidebar.Category(category)).ToContainTextAsync(name);
        }
        await Expect(Sidebar.Category(SeedIds.KbCategories.Laws).GetByTestId(KbTestIds.Count(SeedIds.KbEntries.GoodhartsLaw)))
            .ToBeVisibleAsync();
        await Expect(Sidebar.Count(SeedIds.KbEntries.GoodhartsLaw)).ToHaveTextAsync(new Regex(@"^\d+$"));
        await Expect(Sidebar.Section("series")).ToHaveCountAsync(0);
    }

    [Test, Order(2)]
    public async Task SeriesBook_ShouldShowCharactersAndPlacesFromSeries()
    {
        await Page.GotoAsync(Links.Plan(SeedIds.Books.HollowThrone.ToString()));

        await Expect(Sidebar.Section("series")).ToContainTextAsync("From The Ashen Crown");
        await Expect(Sidebar.Section("series").GetByTestId(KbTestIds.Category(SeedIds.KbCategories.AshenCrownCharacters)))
            .ToContainTextAsync("Characters");
        await Expect(Sidebar.Section("series").GetByTestId(KbTestIds.Category(SeedIds.KbCategories.AshenCrownPlaces)))
            .ToContainTextAsync("Places");
        await Expect(Sidebar.Category(SeedIds.KbCategories.AshenCrownCharacters)).ToContainTextAsync("Characters");
        await Expect(Sidebar.Category(SeedIds.KbCategories.AshenCrownPlaces)).ToContainTextAsync("Places");
        await Expect(Sidebar.Category(SeedIds.KbCategories.Laws)).ToHaveCountAsync(0);

        await Page.GotoAsync(Links.Plan(SeedIds.Books.PlayersGuide.ToString()));

        await Expect(Sidebar.Section("series")).ToContainTextAsync("From Forsaken Gods");
        await Expect(Sidebar.Section("series").GetByTestId(KbTestIds.Count(SeedIds.KbEntries.Gravetouch))).ToBeVisibleAsync();
        await Expect(Sidebar.Item(SeedIds.KbEntries.HollowPrayer)).ToBeVisibleAsync();
    }

    [Test, Order(3)]
    public async Task Northbound_ShouldBeEmpty()
    {
        await Page.GotoAsync(Links.Plan(SeedIds.Books.Northbound.ToString()));

        await Expect(Sidebar.Empty).ToBeVisibleAsync();
        await Expect(Sidebar.Search).ToHaveValueAsync("");
    }

    [Test, Order(4)]
    public async Task Entry_WhenClicked_ShouldSetKbHashOpenWindowAndStoreHighlight()
    {
        var bookId = SeedIds.Books.ProductEngineering.ToString();
        var entryId = SeedIds.KbEntries.GoodhartsLaw.ToString();
        await Page.GotoAsync(Links.Plan(bookId, Links.Hash(("chapter", "4"))));

        await Sidebar.Item(SeedIds.KbEntries.GoodhartsLaw).ClickAsync();

        await Expect(Sidebar.Item(SeedIds.KbEntries.GoodhartsLaw)).ToHaveAttributeAsync("aria-current", "true");
        await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(Links.Plan(bookId, Links.Hash(("chapter", "4"), ("kb", entryId), ("entry", entryId)))) + "$"));
        await Expect(Sidebar.EntryWindow).ToBeVisibleAsync();
        var stored = await Page.EvaluateAsync<string?>("id => sessionStorage.getItem('codex.hl.' + id)", bookId);
        stored.ShouldBe(entryId);
    }

    [Test, Order(5)]
    public async Task Search_ShouldFilterEntries()
    {
        await Page.GotoAsync(Links.Plan(SeedIds.Books.ProductEngineering.ToString()));

        await Sidebar.Search.FillAsync("conway");

        await Expect(Sidebar.Item(SeedIds.KbEntries.ConwaysLaw)).ToBeVisibleAsync();
        await Expect(Sidebar.Item(SeedIds.KbEntries.GoodhartsLaw)).ToHaveCountAsync(0);

        await Sidebar.Search.FillAsync("bad scrum");
        await Expect(Sidebar.Item(SeedIds.KbEntries.SignsOfBadScrum)).ToBeVisibleAsync();

        await Sidebar.Search.FillAsync("zzzz");
        await Expect(Sidebar.NoMatch).ToBeVisibleAsync();
    }

    [Test, Order(6)]
    public async Task Filter_WhenCategoryUnchecked_ShouldHideItsEntries()
    {
        await Page.GotoAsync(Links.Plan(SeedIds.Books.ProductEngineering.ToString()));

        await Sidebar.Filter.ClickAsync();
        await Sidebar.FilterOption(SeedIds.KbCategories.Laws).UncheckAsync();

        await Expect(Sidebar.Category(SeedIds.KbCategories.Laws)).ToHaveCountAsync(0);
        await Expect(Sidebar.Item(SeedIds.KbEntries.GoodhartsLaw)).ToHaveCountAsync(0);
        await Expect(Sidebar.Category(SeedIds.KbCategories.People)).ToBeVisibleAsync();
        await Expect(Sidebar.Filter).ToHaveAttributeAsync("data-active", "true");

        await Sidebar.FilterShowAll.ClickAsync();
        await Expect(Sidebar.Item(SeedIds.KbEntries.GoodhartsLaw)).ToBeVisibleAsync();
    }

    [Test, Order(7)]
    public async Task EntryWindow_WhenEntryInHash_ShouldOpenAndCloseKeepingHighlight()
    {
        var bookId = SeedIds.Books.ProductEngineering.ToString();
        var entryId = SeedIds.KbEntries.ConwaysLaw.ToString();
        await Page.GotoAsync(Links.Plan(bookId, Links.Hash(("kb", entryId), ("entry", entryId))));

        await Expect(Sidebar.EntryWindowTitle).ToHaveTextAsync("Conway's Law");
        await Sidebar.EntryWindowClose.ClickAsync();

        await Expect(Sidebar.EntryWindow).ToHaveCountAsync(0);
        await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(Links.Plan(bookId, Links.Hash(("kb", entryId)))) + "$"));
    }
}
