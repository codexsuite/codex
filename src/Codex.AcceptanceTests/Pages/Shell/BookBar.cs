using Codex.TestSupport;

namespace Codex.AcceptanceTests.Pages.Shell;

// The bar above every book screen: back link, tabs, and the slots lanes fill.
public sealed class BookBar(IPage page)
{
    public ILocator Root => page.GetByTestId(TestIds.Shell.BookBar);

    public ILocator BackLink => page.GetByTestId(TestIds.Shell.BackLink);

    public ILocator TabPlan => page.GetByTestId(TestIds.Shell.TabPlan);

    public ILocator TabWrite => page.GetByTestId(TestIds.Shell.TabWrite);

    public ILocator TabReferences => page.GetByTestId(TestIds.Shell.TabReferences);

    // The References tab's count badge, drawn from the book header on every book screen.
    public ILocator ReferencesBadge => page.GetByTestId(ShellTestIds.ReferencesBadge);

    public ILocator TabLayout => page.GetByTestId(TestIds.Shell.TabLayout);

    public ILocator TabSettings => page.GetByTestId(TestIds.Shell.TabSettings);

    // The current user's initials at the end of the bar (GET /api/auth/me).
    public ILocator Avatar => Root.GetByTestId(ShellTestIds.Avatar);

    public ILocator PaletteButton => Root.GetByTestId(ShellTestIds.PaletteButton);

    // Below write (#80): View only / Comment only, in the save status's place; its title names the owner.
    public ILocator RoleChip => Root.GetByTestId(TestIds.Shell.RoleChip);

    public ILocator SaveStatusSlot => page.GetByTestId(TestIds.Shell.SaveStatusSlot);

    public ILocator BadgeSlot => page.GetByTestId(TestIds.Shell.BadgeSlot);

    public ILocator ScopeSlot => page.GetByTestId(TestIds.Shell.ScopeSlot);

    public ILocator FormatSlot => page.GetByTestId(TestIds.Shell.FormatSlot);

    public ILocator FocusSlot => page.GetByTestId(TestIds.Shell.FocusSlot);

    public ILocator PaletteSlot => page.GetByTestId(TestIds.Shell.PaletteSlot);

    public ILocator CommandPalette => page.GetByTestId(TestIds.Shell.CommandPalette);

    public ILocator LanePlaceholder(string lane) => page.GetByTestId(TestIds.Shell.LanePlaceholder(lane));

    public ILocator ErrorUnknownBook => page.GetByTestId(TestIds.Shell.ErrorUnknownBook);

    public ILocator ErrorBadLink => page.GetByTestId(TestIds.Shell.ErrorBadLink);
}
