using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.AcceptanceTests.Pages.Shell;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Library;

// From the seed: the Forsaken Gods stack opens its screen (totals, books), and a book opened from it links back.
[TestFixture]
public class SeriesScreenTests : SeededAcceptanceTest
{
    private SeriesScreen Series => new(Page);

    [Test, Order(1)]
    public async Task Stack_WhenClicked_ShouldOpenSeriesScreen()
    {
        await Page.GotoAsync(Links.Library());

        await new LibraryShelf(Page).SeriesStack(SeedIds.Series.ForsakenGods.ToString()).ClickAsync();

        await Expect(Series.Root).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"#series={SeedIds.Series.ForsakenGods}"));
        await Expect(Series.Title).ToHaveTextAsync("Forsaken Gods");
        await Expect(Series.Breadcrumb).ToContainTextAsync("Library");
        await Expect(Series.StatBooks).ToContainTextAsync("3");
        await Expect(Series.StatWords).ToContainTextAsync("2,025");
        await Expect(Series.Tile(SeedIds.Books.PlayersGuide.ToString())).ToBeVisibleAsync();
        await Expect(Series.Tile(SeedIds.Books.GamemastersCodex.ToString())).ToBeVisibleAsync();
        await Expect(Series.Tile(SeedIds.Books.BestiaryOfTheFallen.ToString())).ToBeVisibleAsync();
        await Expect(Series.AddBook).ToBeVisibleAsync();
    }

    [Test, Order(2)]
    public async Task Book_WhenOpenedFromSeries_ShouldLinkBackToSeries()
    {
        await Page.GotoAsync(Links.Series(SeedIds.Series.ForsakenGods.ToString()));

        await Series.Tile(SeedIds.Books.PlayersGuide.ToString()).ClickAsync();

        var back = new BookBar(Page).BackLink;
        await Expect(back).ToBeVisibleAsync();
        await Expect(back).ToHaveAttributeAsync("href", Links.Series(SeedIds.Series.ForsakenGods.ToString()));
    }
}
