namespace Codex.Application.Mcp;

// The result of a write (#20 section 2.2 with M11): the scene's new prose version when the write changed prose,
// plain sentences the agent can relay (`side_effects`), the same changes in a form it can act on (`affected`), and the
// id that undoes the write. No phase-1 tool writes; L9.2's write tools derive from this.
public abstract record McpWriteResult : McpResult
{
    public int? Version { get; init; }

    public IReadOnlyList<string> SideEffects { get; init; } = [];

    public IReadOnlyList<McpAffected> Affected { get; init; } = [];

    public string? UndoId { get; init; }
}
