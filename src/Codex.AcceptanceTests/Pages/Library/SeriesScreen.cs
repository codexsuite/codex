using Codex.TestSupport;
using Codex.AcceptanceTests.Pages.Shell;

namespace Codex.AcceptanceTests.Pages.Library;

// The Series screen and its settings dialog.
public sealed class SeriesScreen(IPage page)
{
    public ILocator Root => page.GetByTestId(TestIds.Shell.SeriesScreen);

    public ILocator Title => page.GetByTestId(LibraryTestIds.SeriesTitle);

    public ILocator Breadcrumb => page.GetByTestId(LibraryTestIds.SeriesBreadcrumb);

    public ILocator BackLink => page.GetByTestId(TestIds.Shell.BackLink);

    public ILocator StatBooks => page.GetByTestId(LibraryTestIds.SeriesStatBooks);

    public ILocator StatWords => page.GetByTestId(LibraryTestIds.SeriesStatWords);

    public ILocator StatPublished => page.GetByTestId(LibraryTestIds.SeriesStatPublished);

    public ILocator StatLastEdit => page.GetByTestId(LibraryTestIds.SeriesStatLastEdit);

    public ILocator SettingsButton => page.GetByTestId(LibraryTestIds.SeriesSettingsButton);

    public ILocator AddBook => page.GetByTestId(LibraryTestIds.SeriesAddBook);

    public ILocator Dialog => page.GetByTestId(LibraryTestIds.SeriesDialog);

    public ILocator NameInput => page.GetByTestId(LibraryTestIds.SeriesNameInput);

    public ILocator NameError => page.GetByTestId(LibraryTestIds.SeriesNameError);

    public ILocator Save => page.GetByTestId(LibraryTestIds.SeriesSave);

    public ILocator Cancel => page.GetByTestId(LibraryTestIds.SeriesCancel);

    public ILocator Tile(string bookId) => page.GetByTestId(TestIds.Shell.Tile(bookId));

    public ILocator Grip(string bookId) => page.GetByTestId(LibraryTestIds.SeriesGrip(bookId));

    public ILocator OrderNumber(string bookId) => page.GetByTestId(LibraryTestIds.SeriesOrderNumber(bookId));

    public ILocator MoveUp(string bookId) => page.GetByTestId(LibraryTestIds.SeriesMoveUp(bookId));

    public ILocator DeleteButton => page.GetByTestId(LibraryTestIds.SeriesDeleteButton);

    public ILocator DeleteDialog => page.GetByTestId(LibraryTestIds.SeriesDeleteDialog);

    public ILocator DeleteImpact => page.GetByTestId(LibraryTestIds.SeriesDeleteImpact);

    public ILocator DeleteConfirm => page.GetByTestId(LibraryTestIds.SeriesDeleteConfirm);

    public ILocator DeleteCancel => page.GetByTestId(LibraryTestIds.SeriesDeleteCancel);
}
