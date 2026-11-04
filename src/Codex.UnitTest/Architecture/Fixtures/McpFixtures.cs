// A feature that uses the MCP server on purpose, so the "MCP is a leaf" rule can be shown to fail on a violation.
// It lives in the test assembly, which the architecture tests do not scan as production.
namespace Codex.Application.Features.Yak
{
    public class YakUsesMcp
    {
        public Codex.Application.Mcp.McpItem? Item { get; set; }
    }
}
