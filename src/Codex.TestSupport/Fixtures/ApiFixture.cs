using Codex.Application.Identity;
using Codex.TestSupport.Database;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Codex.TestSupport.Fixtures;

// Base class for API tests. A test class is one scenario on one database: the fixture clones the seed
// template, starts its own Kestrel instance of the app against the clone, and tears both down at the end.
// Tests inside run in [Order]; independent scenarios are separate classes, and classes run in parallel.
// This departs from the template's one-app-per-run [SetUpFixture] on purpose: isolation per scenario.
public abstract class ApiFixture
{
    private ClonedDatabase _database = null!;
    private CodexHost _host = null!;
    private IDisposable? _acting;

    protected HttpClient Client { get; private set; } = null!;

    protected CodexHost Host => _host;

    protected string ConnectionString => _database.ConnectionString;

    // Doubles go here; they are applied through the host's ConfigureTestServices.
    protected virtual Action<IServiceCollection>? ConfigureTestServices => null;

    // Who requests without a sign-in cookie act as (TestAuthentication); override for user B or Anonymous.
    protected virtual TestUser SignedInAs => TestUser.Jd;

    // Override to start from the bare schema (no seed rows).
    protected virtual Task<ClonedDatabase> CloneDatabaseAsync() => CodexDatabase.CloneSeedAsync();

    protected Task<NpgsqlConnection> OpenDatabaseAsync() => _database.OpenAsync();

    [OneTimeSetUp]
    public async Task StartScenarioAsync()
    {
        _database = await CloneDatabaseAsync();
        _host = CodexHost.Start(_database.ConnectionString, SignedInAs, ConfigureTestServices);
        Client = _host.CreateClient();
    }

    // A test that calls a service straight from Host.Services (no request) acts as SignedInAs, as a request would.
    [SetUp]
    public void ActAsSignedInUser() => _acting = SignedInAs.Id is { } id ? CurrentUser.ActAs(id) : null;

    [TearDown]
    public void StopActing() => _acting?.Dispose();

    [OneTimeTearDown]
    public async Task EndScenarioAsync()
    {
        Client?.Dispose();
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
    }
}
