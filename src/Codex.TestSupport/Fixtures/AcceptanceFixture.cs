using System.Text.RegularExpressions;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Codex.TestSupport.Fixtures;

// Base class for browser tests. Same shape as ApiFixture (one cloned database and one Kestrel app per
// test class) plus a browser context per class from the run's shared Chromium and a fresh page per test.
// A trace is recorded per test and kept under artifacts/playwright-traces/ only when the test fails.
// The tests drive the shipped bundle, so build the client first:
//   cd src/Codex.Client && pnpm install && pnpm run build     (emits into src/Codex.WebApi/wwwroot)
public abstract partial class AcceptanceFixture
{
    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;
    private IBrowserContext _context = null!;

    protected IPage Page { get; private set; } = null!;

    protected CodexHost Host => _host;

    protected string BaseUrl => _host.BaseUrl;

    protected string ConnectionString => _database.ConnectionString;

    protected virtual Action<IServiceCollection>? ConfigureTestServices => null;

    // Who requests without a sign-in cookie act as (TestAuthentication); override for user B or Anonymous.
    protected virtual TestUser SignedInAs => TestUser.Jd;

    protected virtual Task<ClonedDatabase> CloneDatabaseAsync() => CodexDatabase.CloneSeedAsync();

    protected static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    protected static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    [OneTimeSetUp]
    public async Task StartScenarioAsync()
    {
        _database = await CloneDatabaseAsync();
        _host = CodexHost.Start(_database.ConnectionString, SignedInAs, ConfigureTestServices);
        Assert.That(
            _host.HasClientBundle(),
            "The SPA bundle is missing. Build the client first: cd src/Codex.Client && pnpm install && pnpm run build "
            + "(it emits into src/Codex.WebApi/wwwroot).");
        var browser = await SharedBrowser.GetAsync();
        _context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _host.BaseUrl,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            TimezoneId = "UTC",
        });
        await _context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
    }

    [SetUp]
    public async Task StartTestAsync()
    {
        await _context.Tracing.StartChunkAsync();
        Page = await _context.NewPageAsync();
    }

    [TearDown]
    public async Task EndTestAsync()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed;
        string? tracePath = null;
        if (failed)
        {
            var name = UnsafeFileChars().Replace(TestContext.CurrentContext.Test.FullName, "_");
            Directory.CreateDirectory(Path.Combine(RepoPaths.ArtifactsDirectory, "playwright-traces"));
            tracePath = Path.Combine(RepoPaths.ArtifactsDirectory, "playwright-traces", $"{name}.zip");
        }
        await _context.Tracing.StopChunkAsync(new TracingStopChunkOptions { Path = tracePath });
        await Page.CloseAsync();
    }

    [OneTimeTearDown]
    public async Task EndScenarioAsync()
    {
        if (_context != null)
        {
            await _context.CloseAsync();
        }
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeFileChars();
}
