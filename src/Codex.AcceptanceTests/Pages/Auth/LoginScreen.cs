namespace Codex.AcceptanceTests.Pages.Auth;

// /login.
public sealed class LoginScreen(IPage page)
{
    public ILocator Root => page.GetByTestId(AuthTestIds.LoginPage);

    public ILocator Error => page.GetByTestId(AuthTestIds.LoginError);

    public async Task SignInAsync(string email, string password)
    {
        await page.GetByTestId(AuthTestIds.LoginEmail).FillAsync(email);
        await page.GetByTestId(AuthTestIds.LoginPassword).FillAsync(password);
        await page.GetByTestId(AuthTestIds.LoginSubmit).ClickAsync();
    }
}
