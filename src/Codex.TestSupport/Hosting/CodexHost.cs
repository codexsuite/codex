using Codex.Application.Database;
using Codex.Application.Features.Ai.Integrations;
using Codex.Application.Features.Print.Render;
using Codex.Application.Identity;
using Codex.WebApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.TestSupport.Hosting;

// The real app (WebApi's Program and DI) on Kestrel at http://127.0.0.1:<free port>, against one database.
// A browser can reach it, and it serves the SPA bundle that `pnpm run build` emitted into
// src/Codex.WebApi/wwwroot. The only seam is configureTestServices, which runs after Program.cs has
// registered everything: that is where a test swaps a double (a fake prose generator, a source lookup). Every host also
// gets TestAuthentication there, signed in as `user` (JD unless the fixture says otherwise).
public sealed class CodexHost : IAsyncDisposable
{
    // The config key the blob store reads (compose sets /data/blobs; appsettings.Development.json a folder
    // under artifacts/). Each host gets its own empty folder, deleted on dispose.
    public const string BlobRootKey = "CODEX_BLOB_ROOT";

    private readonly WebApplicationFactory<ApiMarker> _factory;

    private CodexHost(WebApplicationFactory<ApiMarker> factory, string baseUrl, string blobRoot)
    {
        _factory = factory;
        BaseUrl = baseUrl;
        BlobRoot = blobRoot;
    }

    public string BaseUrl { get; }

    public string BlobRoot { get; }

    public IServiceProvider Services => _factory.Services;

    public static CodexHost Start(string connectionString, Action<IServiceCollection>? configureTestServices = null)
        => Start(connectionString, TestUser.Jd, configureTestServices);

    public static CodexHost Start(string connectionString, TestUser user, Action<IServiceCollection>? configureTestServices = null)
    {
        var blobRoot = Path.Combine(Path.GetTempPath(), "codex-blobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(blobRoot);
        var factory = new WebApplicationFactory<ApiMarker>().WithWebHostBuilder(builder =>
        {
            builder.UseContentRoot(RepoPaths.WebApiProject);
            // Port 0: the OS picks a free port. UseKestrel(0) alone leaves the app on the default :5000.
            builder.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseServices.ConnectionKey] = connectionString,
                [BlobRootKey] = blobRoot,
                // AI integration keys are encrypted with a key ring in this host's own folder (#36).
                [AiSettings.DataProtectionDirKey] = Path.Combine(blobRoot, "data-protection"),
                // One process does both halves, so a proof queued through the API is compiled by this host's worker.
                [CodexRole.Key] = CodexRole.All,
                // Nor may a host's admin bootstrap or MCP user settings.
                [AdminBootstrap.EmailKey] = string.Empty,
                [AdminBootstrap.PasswordKey] = string.Empty,
                [McpUser.EmailKey] = string.Empty,
                [McpUser.TokenKey] = string.Empty,
            }));
            builder.ConfigureTestServices(services => TestAuthentication.Add(services, user));
            if (configureTestServices != null)
            {
                builder.ConfigureTestServices(configureTestServices);
            }
        });
        factory.UseKestrel(0);
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new CodexHost(factory, $"http://127.0.0.1:{new Uri(address).Port}", blobRoot);
    }

    public HttpClient CreateClient() => new() { BaseAddress = new Uri(BaseUrl) };

    // True when `pnpm run build` has emitted the SPA the acceptance tests drive.
    public bool HasClientBundle()
    {
        var webRoot = _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        return File.Exists(Path.Combine(webRoot ?? string.Empty, "index.html"));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(BlobRoot))
        {
            Directory.Delete(BlobRoot, recursive: true);
        }
    }
}
