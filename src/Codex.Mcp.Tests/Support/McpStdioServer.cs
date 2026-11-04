using Codex.Application.Database;
using Codex.Application.Identity;
using Codex.Application.Storage;
using Codex.TestSupport.Database;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Support;

// One test class's MCP server: a clone of the seed database and the real Codex.Mcp process (built next to the tests)
// spawned against it, spoken to over stdio like Claude Desktop does. Disposing stops the process, then drops the clone.
// Test classes carry [CancelAfter] and McpCalls passes the test's token, so a hung process fails the test, not the job.
public sealed class McpStdioServer : IAsyncDisposable
{
    private readonly ClonedDatabase _database;

    private McpStdioServer(ClonedDatabase database, McpClient client)
    {
        _database = database;
        Client = client;
    }

    public McpClient Client { get; }

    public string ConnectionString => _database.ConnectionString;

    // How long the child process may take to start and answer initialize before the fixture fails.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    public static Task<McpStdioServer> StartAsync() =>
        StartAsync(Path.Combine(AppContext.BaseDirectory, "Codex.Mcp.dll"), _ => { });

    // serverDll: the process to spawn; cloned: told of the clone before the process starts (McpStdioServerTests).
    public static Task<McpStdioServer> StartAsync(string serverDll, Action<ClonedDatabase> cloned)
        => StartAsync(serverDll, database =>
        {
            cloned(database);
            return Task.FromResult(new Identity(null, null));
        });

    // The process acting as another user (CODEX_MCP_USER_EMAIL): createUser adds that user to the clone before the
    // process starts and returns the email (McpOwnershipTests).
    public static Task<McpStdioServer> StartAsUserAsync(Func<ClonedDatabase, Task<string>> createUser)
        => StartAsync(Path.Combine(AppContext.BaseDirectory, "Codex.Mcp.dll"), async database => new Identity(await createUser(database), null));

    // The process acting through a personal access token (CODEX_MCP_TOKEN, #32): createToken adds the token to the
    // clone before the process starts and returns its secret (McpStdioTokenTests).
    public static Task<McpStdioServer> StartWithTokenAsync(
        Func<ClonedDatabase, Task<string>> createToken, IReadOnlyDictionary<string, string>? environment = null)
        => StartAsync(Path.Combine(AppContext.BaseDirectory, "Codex.Mcp.dll"), async database => new Identity(null, await createToken(database)), environment);

    // The default process plus extra environment (J12: CODEX_TEMPLATES_ROOT, which the print tools' preflight reads).
    public static Task<McpStdioServer> StartWithEnvironmentAsync(IReadOnlyDictionary<string, string> environment) =>
        StartAsync(Path.Combine(AppContext.BaseDirectory, "Codex.Mcp.dll"), _ => Task.FromResult(new Identity(null, null)), environment);

    private sealed record Identity(string? Email, string? Token);

    private static async Task<McpStdioServer> StartAsync(
        string serverDll, Func<ClonedDatabase, Task<Identity>> cloned, IReadOnlyDictionary<string, string>? environment = null)
    {
        var database = await CodexDatabase.CloneSeedAsync();
        try
        {
            var identity = await cloned(database);
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "codex",
                Command = "dotnet",
                Arguments = [serverDll],
                WorkingDirectory = AppContext.BaseDirectory,
                EnvironmentVariables = new Dictionary<string, string?>(environment?.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)) ?? [])
                {
                    [DatabaseServices.ConnectionKey] = database.ConnectionString,
                    [DatabaseServices.MigrateKey] = "false",
                    // Blank (JD) unless the test names another user or a token, so a value exported on the host never leaks in.
                    [McpUser.EmailKey] = identity.Email ?? string.Empty,
                    [McpUser.TokenKey] = identity.Token ?? string.Empty,
                    [StorageServices.BlobRootKey] = Path.Combine(Path.GetTempPath(), "codex-mcp-tests-blobs"),
                },
            });
            using var timeout = new CancellationTokenSource(StartTimeout);
            var client = await McpClient.CreateAsync(transport, new McpClientOptions(), cancellationToken: timeout.Token);
            return new McpStdioServer(database, client);
        }
        catch
        {
            // The process never answered: drop the clone rather than leak it.
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _database.DisposeAsync();
    }
}
