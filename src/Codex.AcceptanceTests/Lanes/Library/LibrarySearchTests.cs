using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Library;

// Searching the shelf from the seed. "under" matches Undertow, so only the Tidewater stack stays.
[TestFixture]
public class LibrarySearchTests : SeededAcceptanceTest
{
    private LibraryScreen Library => new(Page);

    [Test, Order(1)]
    public async Task Search_WhenTypingUnder_ShouldFilterStacksAndShelf()
    {
        await Page.GotoAsync(Links.Library());
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.ForsakenGods.ToString())).ToBeVisibleAsync();

        var response = await Page.RunAndWaitForApiAsync("/api/library", "GET",
            () => Library.Shelf.SearchBox.FillAsync("under"));

        response.Url.ShouldContain("q=under");
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.TidewaterChronicles.ToString())).ToBeVisibleAsync();
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.ForsakenGods.ToString())).ToHaveCountAsync(0);
        await Expect(Library.Shelf.SeriesStack(SeedIds.Series.AshenCrown.ToString())).ToHaveCountAsync(0);
        await Expect(Library.StackGroup.GetByTestId(LibraryTestIds.SeriesName)).ToHaveCountAsync(1);
        await Expect(Library.StandaloneGroup).ToHaveTextAsync(string.Empty);
    }

    [Test, Order(2)]
    public async Task Search_WhenNothingMatches_ShouldShowNoResults()
    {
        await Page.GotoAsync(Links.Library());

        await Library.Shelf.SearchBox.FillAsync("zzzzqqq");

        await Expect(Library.NoResults).ToContainTextAsync("No books match \"zzzzqqq\".");
        await Expect(Library.Shelf.SearchBox).ToHaveValueAsync("zzzzqqq");
        await Library.ClearSearch.ClickAsync();
        await Expect(Library.NoResults).ToHaveCountAsync(0);
        await Expect(Library.Shelf.Tile(SeedIds.Books.ProductEngineering.ToString())).ToBeVisibleAsync();
    }
}
