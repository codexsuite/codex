namespace Codex.Application.Mcp;

// Registered only by the Codex.Mcp (stdio) process: one client per process, so that process is one session for
// stale_ref (McpRefs.SessionKey). The web host's stateless /mcp never registers it.
public sealed record McpStdioTransport(string SessionKey)
{
    public static McpStdioTransport Default { get; } = new("stdio");
}
