namespace Codex.AcceptanceTests.Pages.Library;

// The New book dialog and the Library actions that open it.
public sealed class NewBookDialog(IPage page)
{
    public ILocator OpenButton => page.GetByTestId(LibraryTestIds.NewBook);

    public ILocator Root => page.GetByTestId(LibraryTestIds.NewBookDialog);

    public ILocator Title => page.GetByTestId(LibraryTestIds.NewBookTitle);

    public ILocator TitleError => page.GetByTestId(LibraryTestIds.NewBookTitleError);

    public ILocator Series => page.GetByTestId(LibraryTestIds.NewBookSeries);

    public ILocator Number => page.GetByTestId(LibraryTestIds.NewBookNumber);

    public ILocator Target => page.GetByTestId(LibraryTestIds.NewBookTarget);

    public ILocator Error => page.GetByTestId(LibraryTestIds.NewBookError);

    public ILocator Create => page.GetByTestId(LibraryTestIds.NewBookCreate);

    public ILocator Cancel => page.GetByTestId(LibraryTestIds.NewBookCancel);
}
