using Codex.TestSupport;

namespace Codex.AcceptanceTests.Pages.Shell;

// The page-level entry point: the app root plus the three shell page objects.
public sealed class AppShell(IPage page)
{
    public ILocator Root => page.GetByTestId(TestIds.Shell.AppShell);

    public LibraryShelf Library { get; } = new(page);

    public BookBar BookBar { get; } = new(page);

    public KbSidebarFrame KbSidebar { get; } = new(page);
}
