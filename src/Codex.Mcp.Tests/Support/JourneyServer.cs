using System.Net.Http.Json;
using System.Text.Json;
using Codex.Application.Features.References;
using Codex.TestSupport.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Dapper;
using Npgsql;
using ModelContextProtocol.Client;

namespace Codex.Mcp.Tests.Support;

// One journey class's world: the real Codex.Mcp process over stdio (Client) and, on the same clone of the seed, the real
// web host (Api), so a test can check, as the UI would, that an agent's write is visible through the HTTP API.
public sealed class JourneyServer : IAsyncDisposable
{
    private readonly McpStdioServer _stdio;
    private readonly CodexHost _host;
    private readonly HttpClient _http;

    private JourneyServer(McpStdioServer stdio, CodexHost host)
    {
        _stdio = stdio;
        _host = host;
        _http = host.CreateClient();
    }

    public McpClient Client => _stdio.Client;

    public string ConnectionString => _stdio.ConnectionString;

    public static async Task<JourneyServer> StartAsync()
    {
        var stdio = await McpStdioServer.StartAsync();
        try
        {
            return new JourneyServer(stdio, CodexHost.Start(stdio.ConnectionString));
        }
        catch
        {
            await stdio.DisposeAsync();
            throw;
        }
    }

    // As StartAsync, with the stdio process also given the repo's templates/ (CODEX_TEMPLATES_ROOT) so print preflight can resolve a template.
    public static async Task<JourneyServer> StartWithTemplatesAsync()
    {
        var stdio = await McpStdioServer.StartWithEnvironmentAsync(TemplatesEnvironment());
        try
        {
            return new JourneyServer(stdio, CodexHost.Start(stdio.ConnectionString));
        }
        catch
        {
            await stdio.DisposeAsync();
            throw;
        }
    }

    // CODEX_TEMPLATES_ROOT for a stdio process: the repo's templates/ folder, found above the test output.
    public static IReadOnlyDictionary<string, string> TemplatesEnvironment()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "templates", "novel-6x9")))
        {
            root = root.Parent;
        }
        return new Dictionary<string, string>
        {
            ["CODEX_TEMPLATES_ROOT"] = Path.Combine(root?.FullName ?? throw new InvalidOperationException("templates/ not found above the test output."), "templates"),
        };
    }

    // GET /api/... as the SPA would.
    public async Task<JsonElement> ApiAsync(string path)
    {
        var response = await _http.GetAsync(path, McpCalls.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(McpCalls.Token);
    }

    // POST /api/... as the author in the SPA would (an action the UI owns, such as signing a source off), with an optional JSON body.
    public async Task<JsonElement> PostApiAsync(string path, object? body = null)
    {
        var response = body is null ? await _http.PostAsync(path, null, McpCalls.Token) : await _http.PostAsJsonAsync(path, body, McpCalls.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(McpCalls.Token);
    }

    // PATCH /api/... with a JSON body, as the References panel saves a field.
    public async Task<JsonElement> PatchApiAsync(string path, object body)
    {
        var response = await _http.PatchAsJsonAsync(path, body, McpCalls.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(McpCalls.Token);
    }

    // The quote scan the web host runs when a scene is saved, run now, so a test can read its outcome without waiting for the event.
    public Task ScanAsync(Guid book, Guid scene) =>
        _host.Services.GetRequiredService<QuoteFlagScanner>().ScanAsync(book, scene);

    // A scene's stored prose and version, read from the database.
    public async Task<(string Body, int Version)> ProseAsync(Guid scene)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        return await connection.QuerySingleAsync<(string, int)>("SELECT body, version FROM prose WHERE scene_id = @scene", new { scene });
    }

    // One value from the database.
    public async Task<T?> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, args);
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        await _stdio.DisposeAsync();
    }
}
