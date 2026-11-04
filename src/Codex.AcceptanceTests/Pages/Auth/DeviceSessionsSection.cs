namespace Codex.AcceptanceTests.Pages.Auth;

// /account → Signed-in devices: the phones signed in with a device session (#35).
public sealed class DeviceSessionsSection(IPage page)
{
    public ILocator Root => page.GetByTestId(AuthTestIds.DevicesSection);

    public ILocator Empty => page.GetByTestId(AuthTestIds.DevicesEmpty);

    public ILocator Row(string sessionId) => page.GetByTestId(AuthTestIds.DeviceRow(sessionId));

    public ILocator SignOut(string sessionId) => page.GetByTestId(AuthTestIds.DeviceSignOut(sessionId));
}
