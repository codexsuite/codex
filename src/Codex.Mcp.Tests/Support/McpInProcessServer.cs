using System.IO.Pipelines;
using Agoda.IoC.NetCore;
using Codex.Application;
using Codex.Application.Database;
using Codex.Application.Mcp;
using Codex.Contracts.Ai;
using Codex.TestSupport.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Codex.Mcp.Tests.Support;

// The Codex.Mcp server wired as its Program.cs does (Agoda.IoC scan, the stdio modules, no other hosted service, the
// SDK's server over streams) but inside the test process, so the test chooses what IProseGenerator is. The stdio child
// process has no such seam, and production has no switch for one: this exists only in the test project. Each instance
// has its own clone of the seed database.
public sealed class McpInProcessServer : IAsyncDisposable
{
    private readonly ClonedDatabase _database;
    private readonly IHost _host;

    private McpInProcessServer(ClonedDatabase database, IHost host, McpClient client)
    {
        _database = database;
        _host = host;
        Client = client;
    }

    public McpClient Client { get; }

    public IServiceProvider Services => _host.Services;

    public string ConnectionString => _database.ConnectionString;

    public static async Task<McpInProcessServer> StartAsync(IProseGenerator generator, IReadOnlyDictionary<string, string?>? settings = null, string? protocolVersion = null,
        Action<IServiceCollection>? configure = null)
    {
        var database = await CodexDatabase.CloneSeedAsync();
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseServices.ConnectionKey] = database.ConnectionString,
                [DatabaseServices.MigrateKey] = "false",
            });
            builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
            builder.Services.AutoWireAssembly([ApplicationMarker.Assembly], isMockMode: false);
            foreach (var module in StdioModules.Find())
            {
                module.AddServices(builder.Services, builder.Configuration);
            }
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.RemoveAll<IProseGenerator>();
            builder.Services.AddSingleton(generator);
            configure?.Invoke(builder.Services);

            var clientToServer = new Pipe();
            var serverToClient = new Pipe();
            builder.Services.AddCodexMcpServer().WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
            var host = builder.Build();
            await host.StartAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var client = await McpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
                new McpClientOptions { ClientInfo = new Implementation { Name = "test-agent", Version = "1.0.0" }, ProtocolVersion = protocolVersion },
                cancellationToken: timeout.Token);
            return new McpInProcessServer(database, host, client);
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
        await _database.DisposeAsync();
    }
}
