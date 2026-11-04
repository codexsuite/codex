namespace Codex.Application.Mcp;

// One structured change a write caused (M11), next to its side_effects sentence:
// { "kind": "source", "id": "…", "field": "status", "from": "risk", "to": "needs_check" }.
public sealed record McpAffected(string Kind, Guid Id, string Field, string? From, string? To, string? Label = null);
