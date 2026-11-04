namespace Codex.AcceptanceTests.Pages.Auth;

// /settings/users.
public sealed class UsersScreen(IPage page)
{
    public ILocator Root => page.GetByTestId(AuthTestIds.UsersPage);

    public ILocator OneTimePassword => page.GetByTestId(AuthTestIds.OneTimePassword);

    public ILocator Error => page.GetByTestId(AuthTestIds.UsersError);

    public ILocator Row(string userId) => page.GetByTestId(AuthTestIds.UserRow(userId));

    public ILocator Status(string userId) => page.GetByTestId(AuthTestIds.UserStatus(userId));

    public ILocator ToggleAdmin(string userId) => page.GetByTestId(AuthTestIds.UserToggleAdmin(userId));

    public ILocator ToggleDisabled(string userId) => page.GetByTestId(AuthTestIds.UserToggleDisabled(userId));

    public ILocator ResetPassword(string userId) => page.GetByTestId(AuthTestIds.UserResetPassword(userId));

    public ILocator TokenCount(string userId) => page.GetByTestId(AuthTestIds.UserTokenCount(userId));

    public ILocator RevokeTokens(string userId) => page.GetByTestId(AuthTestIds.UserRevokeTokens(userId));

    public async Task AddAsync(string email, string displayName)
    {
        await page.GetByTestId(AuthTestIds.AddUserEmail).FillAsync(email);
        await page.GetByTestId(AuthTestIds.AddUserName).FillAsync(displayName);
        await page.GetByTestId(AuthTestIds.AddUserSubmit).ClickAsync();
    }
}
