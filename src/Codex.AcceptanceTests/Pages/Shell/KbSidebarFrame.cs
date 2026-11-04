using Codex.TestSupport;

namespace Codex.AcceptanceTests.Pages.Shell;

// The Knowledge base sidebar frame that Plan and Write share. The Kb lane fills it; the shell owns the frame.
public sealed class KbSidebarFrame(IPage page)
{
    public ILocator Root => page.GetByTestId(TestIds.Shell.KbSidebar);

    public ILocator List => page.GetByTestId(TestIds.Shell.KbList);

    public ILocator Item(string entryId) => page.GetByTestId(TestIds.Shell.KbItem(entryId));
}
