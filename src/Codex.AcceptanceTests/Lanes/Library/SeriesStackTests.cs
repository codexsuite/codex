using Codex.AcceptanceTests.Harness;
using Codex.AcceptanceTests.Pages.Library;
using Codex.Contracts;

namespace Codex.AcceptanceTests.Lanes.Library;

// The Forsaken Gods stack has three books, Ashen Crown four: both show three covers and the real count.
[TestFixture]
public class SeriesStackTests : SeededAcceptanceTest
{
    private LibraryScreen Library => new(Page);

    [Test, Order(1)]
    public async Task Stack_ShouldShowThreeCoversAndBookCountBadge()
    {
        await Page.GotoAsync(Links.Library());

        var forsaken = Library.Shelf.SeriesStack(SeedIds.Series.ForsakenGods.ToString());
        await Expect(LibraryScreen.Covers(forsaken)).ToHaveCountAsync(3);
        await Expect(LibraryScreen.StackCount(forsaken)).ToHaveTextAsync("3");
        var ashen = Library.Shelf.SeriesStack(SeedIds.Series.AshenCrown.ToString());
        await Expect(LibraryScreen.Covers(ashen)).ToHaveCountAsync(3);
        await Expect(LibraryScreen.StackCount(ashen)).ToHaveTextAsync("4");
        var tidewater = Library.Shelf.SeriesStack(SeedIds.Series.TidewaterChronicles.ToString());
        await Expect(LibraryScreen.Covers(tidewater)).ToHaveCountAsync(2);
        await Expect(LibraryScreen.StackCount(tidewater)).ToHaveTextAsync("2");
    }

    [Test, Order(2)]
    public async Task Stack_WhenClicked_ShouldOpenSeriesInTheHash()
    {
        await Page.GotoAsync(Links.Library());

        await Library.Shelf.SeriesStack(SeedIds.Series.ForsakenGods.ToString()).ClickAsync();

        await Expect(Library.Shelf.SeriesScreen).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(
            $"#series={SeedIds.Series.ForsakenGods}"));
    }
}
