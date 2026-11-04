namespace Codex.AcceptanceTests.Pages.Auth;

// /account/password, and the same form on /account.
public sealed class PasswordScreen(IPage page)
{
    public ILocator Root => page.GetByTestId(AuthTestIds.PasswordPage);

    public ILocator MustChange => page.GetByTestId(AuthTestIds.PasswordMustChange);

    public ILocator Error => page.GetByTestId(AuthTestIds.PasswordError);

    public ILocator Saved => page.GetByTestId(AuthTestIds.PasswordSaved);

    public async Task ChangeAsync(string current, string next, string? confirm = null)
    {
        await page.GetByTestId(AuthTestIds.PasswordCurrent).FillAsync(current);
        await page.GetByTestId(AuthTestIds.PasswordNew).FillAsync(next);
        await page.GetByTestId(AuthTestIds.PasswordConfirm).FillAsync(confirm ?? next);
        await page.GetByTestId(AuthTestIds.PasswordSubmit).ClickAsync();
    }
}
