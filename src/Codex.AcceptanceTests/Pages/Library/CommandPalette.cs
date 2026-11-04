using Codex.AcceptanceTests.Pages.Shell;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Pages.Library;

// The ⌘K palette's Library content (books, series, actions) inside the shell's frame.
public sealed class CommandPalette(IPage page)
{
    public ILocator Button => page.GetByTestId(ShellTestIds.PaletteButton);

    public ILocator Root => page.GetByTestId(TestIds.Shell.CommandPalette);

    public ILocator Input => page.GetByTestId(LibraryTestIds.PaletteInput);

    public ILocator Empty => page.GetByTestId(LibraryTestIds.PaletteEmpty);

    public ILocator Book(string bookId) => page.GetByTestId(LibraryTestIds.PaletteBook(bookId));

    public ILocator Series(string seriesId) => page.GetByTestId(LibraryTestIds.PaletteSeries(seriesId));

    public ILocator Action(string action) => page.GetByTestId(LibraryTestIds.PaletteAction(action));
}
