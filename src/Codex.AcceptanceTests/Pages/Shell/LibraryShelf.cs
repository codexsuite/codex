using Codex.TestSupport;

namespace Codex.AcceptanceTests.Pages.Shell;

// The Library screen: shelf of book tiles and series stacks, list view, and the controls above them.
public sealed class LibraryShelf(IPage page)
{
    public ILocator Root => page.GetByTestId(TestIds.Shell.LibraryPage);

    public ILocator Shelf => page.GetByTestId(TestIds.Shell.Shelf);

    public ILocator ListView => page.GetByTestId(TestIds.Shell.ListView);

    public ILocator ViewToggle => page.GetByTestId(TestIds.Shell.ViewToggle);

    public ILocator SearchBox => page.GetByTestId(TestIds.Shell.SearchBox);

    public ILocator SortControl => page.GetByTestId(TestIds.Shell.SortControl);

    public ILocator GroupControl => page.GetByTestId(TestIds.Shell.GroupControl);

    public ILocator Tile(string bookId) => page.GetByTestId(TestIds.Shell.Tile(bookId));

    public ILocator ListRow(string bookId) => page.GetByTestId(TestIds.Shell.ListRow(bookId));

    public ILocator SeriesStack(string seriesId) => page.GetByTestId(TestIds.Shell.SeriesStack(seriesId));

    public ILocator SeriesScreen => page.GetByTestId(TestIds.Shell.SeriesScreen);
}
