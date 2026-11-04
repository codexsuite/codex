using Codex.AcceptanceTests.Pages.Shell;

namespace Codex.AcceptanceTests.Pages.Library;

// The Library lane's additions to the shell's LibraryShelf: stack and standalone groups, list rows, group rows.
public sealed class LibraryScreen(IPage page)
{
    public LibraryShelf Shelf { get; } = new(page);

    public ILocator StackGroup => page.GetByTestId(LibraryTestIds.StackGroup);

    public ILocator StandaloneGroup => page.GetByTestId(LibraryTestIds.StandaloneGroup);

    public ILocator NoResults => page.GetByTestId(LibraryTestIds.NoResults);

    public ILocator ClearSearch => page.GetByTestId(LibraryTestIds.ClearSearch);

    public ILocator GroupRows => page.GetByTestId(LibraryTestIds.GroupRow);

    public ILocator ViewList => page.GetByTestId(LibraryTestIds.ViewList);

    public static ILocator Covers(ILocator within) => within.GetByTestId(LibraryTestIds.Cover);

    public static ILocator StackCount(ILocator within) => within.GetByTestId(LibraryTestIds.StackCount);

    public ILocator StatusFilter => page.GetByTestId(LibraryTestIds.StatusFilter);

    public ILocator NewSeries => page.GetByTestId(LibraryTestIds.NewSeries);

    public ILocator Restore(string bookId) => page.GetByTestId(LibraryTestIds.RestoreBook(bookId));

    public ILocator BookMenu(string bookId) => page.GetByTestId(LibraryTestIds.BookMenu(bookId));

    public ILocator DeleteBookItem(string bookId) => page.GetByTestId(LibraryTestIds.DeleteBookItem(bookId));

    public ILocator DeleteBookDialog => page.GetByTestId(LibraryTestIds.DeleteBookDialog);

    public ILocator DeleteBookImpact => page.GetByTestId(LibraryTestIds.DeleteBookImpact);

    public ILocator DeleteBookTitle => page.GetByTestId(LibraryTestIds.DeleteBookTitle);

    public ILocator DeleteBookConfirm => page.GetByTestId(LibraryTestIds.DeleteBookConfirm);

    public ILocator DeleteBookCancel => page.GetByTestId(LibraryTestIds.DeleteBookCancel);

    public ILocator NewSeriesDialog => page.GetByTestId(LibraryTestIds.NewSeriesDialog);

    public ILocator NewSeriesName => page.GetByTestId(LibraryTestIds.NewSeriesName);

    public ILocator NewSeriesNameError => page.GetByTestId(LibraryTestIds.NewSeriesNameError);

    public ILocator NewSeriesNotice => page.GetByTestId(LibraryTestIds.NewSeriesNotice);

    public ILocator NewSeriesBook(string bookId) => page.GetByTestId(LibraryTestIds.NewSeriesBook(bookId));

    public ILocator NewSeriesCreate => page.GetByTestId(LibraryTestIds.NewSeriesCreate);

    public ILocator NewSeriesCancel => page.GetByTestId(LibraryTestIds.NewSeriesCancel);
}
