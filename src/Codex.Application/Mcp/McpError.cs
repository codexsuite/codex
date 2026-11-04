namespace Codex.Application.Mcp;

// The body of a failed tool call: { "ok": false, "error": { "code", "message", "candidates"?, "field"?, "rule"?, "scope"?, "open_url"? } }.
// scope: for out_of_scope, the book and series that were checked. open_url: for a blocked call the author has to make in the UI, where.
public sealed record McpError(
    string Code, string Message, IReadOnlyList<McpItem>? Candidates, string? Field, string? Rule, IReadOnlyList<McpItem>? Scope = null, string? OpenUrl = null);
