using Codex.Application.Features.References;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Codex.Application.Mcp;

// Who made a write, for provenance (#20 2.5, M12): the MCP session (McpRefs.SessionKey; null over stateless HTTP), the
// client's own name from initialize (clientInfo.name, e.g. "claude-code") and, when the call came with a personal access
// token, that token's id and name (#32).
public sealed record McpCaller(string? Session, string? ClientName, Guid? TokenId = null, string? TokenName = null)
{
    // The History lines the References commands write for this call: 'agent', with the client's and token's names, at
    // insert time.
    public EventProvenance Provenance => EventProvenance.Agent(ClientName, TokenId, TokenName);

    public static McpCaller From(McpServer server)
    {
        var grant = server.Services?.GetService<McpAccess>()?.Current;
        return new(McpRefs.SessionKey(server), server.ClientInfo?.Name, grant?.TokenId, grant?.TokenName);
    }
}
