using System.Text.RegularExpressions;
using Codex.TestSupport;

namespace Codex.AcceptanceTests.Pages.Kb;

// The Knowledge base sidebar on Plan and Write, with the lane's search, categories, counts and entry window.
public sealed class KbSidebar(IPage page)
{
    public ILocator Root => page.GetByTestId(TestIds.Shell.KbSidebar);

    public ILocator Search => page.GetByTestId(KbTestIds.Search);

    public ILocator Empty => page.GetByTestId("shell-kb-empty");

    public ILocator NoMatch => page.GetByTestId(KbTestIds.NoMatch);

    public ILocator Filter => page.GetByTestId(KbTestIds.Filter);

    public ILocator FilterOption(Guid categoryId) => page.GetByTestId(KbTestIds.FilterOption(categoryId));

    public ILocator FilterShowAll => page.GetByTestId(KbTestIds.FilterShowAll);

    public ILocator Section(string key) => page.GetByTestId(KbTestIds.Section(key));

    public ILocator Category(Guid categoryId) => page.GetByTestId(KbTestIds.Category(categoryId));

    public ILocator Item(Guid entryId) => page.GetByTestId(TestIds.Shell.KbItem(entryId.ToString()));

    public ILocator Count(Guid entryId) => page.GetByTestId(KbTestIds.Count(entryId));

    public ILocator EntryWindow => page.GetByTestId(KbTestIds.EntryWindow);

    public ILocator EntryWindowTitle => page.GetByTestId(KbTestIds.EntryWindowTitle);

    public ILocator EntryWindowClose => page.GetByTestId(KbTestIds.EntryWindowClose);

    public ILocator CategoryMenu(Guid categoryId) => page.GetByTestId(KbTestIds.CategoryMenu(categoryId));

    public ILocator CategoryRenameInput => page.GetByTestId(KbTestIds.CategoryRenameInput);

    public ILocator CategoryRenameSave => page.GetByTestId(KbTestIds.CategoryRenameSave);

    public ILocator CategoryColour(string token) => page.GetByTestId(KbTestIds.CategoryColour(token));

    public ILocator CategoryDelete(Guid categoryId) => page.GetByTestId(KbTestIds.CategoryDelete(categoryId));

    public ILocator CategoryMenuError => page.GetByTestId(KbTestIds.CategoryMenuError);

    public ILocator Entries => Root.GetByTestId(new Regex("^shell-kb-item-"));
}
