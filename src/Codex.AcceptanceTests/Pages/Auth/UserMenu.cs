using Codex.AcceptanceTests.Pages.Shell;

namespace Codex.AcceptanceTests.Pages.Auth;

// The avatar menu at the end of every bar (its button is the shell's avatar).
public sealed class UserMenu(IPage page)
{
    public ILocator Button => page.GetByTestId(ShellTestIds.Avatar);

    public ILocator Name => page.GetByTestId(AuthTestIds.UserMenuName);

    public ILocator Account => page.GetByTestId(AuthTestIds.UserMenuAccount);

    public ILocator Ai => page.GetByTestId(AuthTestIds.UserMenuAi);

    public ILocator Users => page.GetByTestId(AuthTestIds.UserMenuUsers);

    public ILocator SignOut => page.GetByTestId(AuthTestIds.UserMenuSignOut);

    public async Task OpenAsync() => await Button.ClickAsync();
}
