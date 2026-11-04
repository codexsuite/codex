using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Mcp;

// The web host's MCP server: streamable HTTP, mapped at /mcp by McpHttpEndpoints. The Codex.Mcp (stdio) process
// skips this module and adds the stdio transport to the same server instead.
public sealed class McpHttpServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
        => services.AddCodexMcpServer().WithHttpTransport();
}
