using Codex.Application;
using Codex.Application.Mcp;
using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Mcp;

// Codex.Application's service modules, as the web host's module scan finds them, minus the HTTP transport, plus this
// process's own: the marker that makes it one stale_ref session (McpStdioTransport).
public static class StdioModules
{
    public static IReadOnlyList<IServiceModule> Find()
        => ApplicationMarker.Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IServiceModule).IsAssignableFrom(t)
                        && t != typeof(McpHttpServices))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IServiceModule)Activator.CreateInstance(t)!)
            .Append(new StdioTransportModule())
            .ToList();

    private sealed class StdioTransportModule : IServiceModule
    {
        public void AddServices(IServiceCollection services, IConfiguration configuration)
            => services.AddSingleton(McpStdioTransport.Default);
    }
}
