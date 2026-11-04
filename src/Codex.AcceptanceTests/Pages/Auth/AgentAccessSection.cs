namespace Codex.AcceptanceTests.Pages.Auth;

// /account → Agent access: personal access tokens for MCP (#32).
public sealed class AgentAccessSection(IPage page)
{
    public ILocator Root => page.GetByTestId(AuthTestIds.TokensSection);

    public ILocator Secret => page.GetByTestId(AuthTestIds.TokenSecret);

    public ILocator Empty => page.GetByTestId(AuthTestIds.TokensEmpty);

    public ILocator Row(string tokenId) => page.GetByTestId(AuthTestIds.TokenRow(tokenId));

    public ILocator Revoke(string tokenId) => page.GetByTestId(AuthTestIds.TokenRevoke(tokenId));

    // scopes: "write", "references" or "generate" to tick besides read; expiry: the option's text ("Never", "90 days").
    public async Task CreateAsync(string name, IEnumerable<string> scopes, string expiry)
    {
        await page.GetByTestId(AuthTestIds.TokenName).FillAsync(name);
        foreach (var scope in scopes)
        {
            await page.GetByTestId(AuthTestIds.TokenScope(scope)).CheckAsync();
        }
        await page.GetByTestId(AuthTestIds.TokenExpiry).SelectOptionAsync(new SelectOptionValue { Label = expiry });
        await page.GetByTestId(AuthTestIds.TokenCreate).ClickAsync();
    }

    public Task DismissSecretAsync() => page.GetByTestId(AuthTestIds.TokenSecretDismiss).ClickAsync();
}
