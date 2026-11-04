using Codex.TestSupport.Fixtures;

namespace Codex.AcceptanceTests;

// Closes the run's shared Chromium when the last acceptance test has finished.
[SetUpFixture]
public class BrowserLifetime
{
    [OneTimeTearDown]
    public async Task CloseBrowserAsync() => await SharedBrowser.DisposeAsync();
}
