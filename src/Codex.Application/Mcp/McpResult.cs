namespace Codex.Application.Mcp;

// Every successful tool result (#20 section 2.2). A tool's result record derives from this and adds its own fields
// next to `ok` and `item`; a write derives from McpWriteResult instead.
public abstract record McpResult
{
    public bool Ok { get; init; } = true;

    // What the call was about, when it was about one thing.
    public McpItem? Item { get; init; }
}
