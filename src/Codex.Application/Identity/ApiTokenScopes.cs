namespace Codex.Application.Identity;

// What a personal access token may do over MCP (#32, from the #20 review). Every token holds Read; McpScopes says
// which scope each tool needs.
public static class ApiTokenScopes
{
    // Every read tool and resource.
    public const string Read = "codex:read";

    // Outline, prose, Knowledge base and label edits, books, drafts the agent wrote, resolve_draft and undo.
    public const string Write = "codex:write";

    // The References tools that write: sources, uses, clearance and permission letters.
    public const string References = "codex:references";

    // draft_prose, which spends the author's model budget.
    public const string Generate = "codex:generate";

    public static readonly IReadOnlyList<string> All = [Read, Write, References, Generate];
}
