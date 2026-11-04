using Microsoft.Playwright;

namespace Codex.TestSupport.Fixtures;

// One Chromium per test run, shared by every acceptance class (each class gets its own context).
public static class SharedBrowser
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;

    public static async Task<IBrowser> GetAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return _browser;
        }
        finally
        {
            Gate.Release();
        }
    }

    // Called once when the test run ends (a [SetUpFixture] one-time teardown in the acceptance assembly).
    public static async Task DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
        }
        _playwright?.Dispose();
        _browser = null;
        _playwright = null;
    }
}
