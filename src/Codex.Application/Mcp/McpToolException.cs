namespace Codex.Application.Mcp;

// A tool call that fails with one of McpErrorCodes. McpToolErrors turns it (and any ApiException a command throws)
// into the error result, so a tool never builds an error body itself. Candidates: the matches for `ambiguous`, the
// close matches for `not_found`, and for `stale_ref` what the session saw before (first) and what the ref names now.
public sealed class McpToolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyList<McpItem>? Candidates { get; init; }

    // `invalid`: the argument at fault. `blocked`: the rule that refused it.
    public string? Field { get; init; }

    public string? Rule { get; init; }

    // `blocked` by a decision that is the author's alone: where in the UI they make it.
    public string? OpenUrl { get; init; }

    // `out_of_scope`: the scope that was checked (the book, then its series).
    public IReadOnlyList<McpItem>? Scope { get; init; }

    public static McpToolException NotFound(string message, IReadOnlyList<McpItem>? closeMatches = null) =>
        new(McpErrorCodes.NotFound, message) { Candidates = closeMatches is { Count: > 0 } ? closeMatches : null };

    public static McpToolException Ambiguous(string message, IReadOnlyList<McpItem> candidates) =>
        new(McpErrorCodes.Ambiguous, message) { Candidates = candidates };

    public static McpToolException StaleRef(string message, McpItem before, McpItem now) =>
        new(McpErrorCodes.StaleRef, message) { Candidates = [before, now] };

    public static McpToolException OutOfScope(string message, IReadOnlyList<McpItem> matches, IReadOnlyList<McpItem> scope) =>
        new(McpErrorCodes.OutOfScope, message) { Candidates = matches, Scope = scope };

    public static McpToolException Invalid(string field, string message) =>
        new(McpErrorCodes.Invalid, message) { Field = field };

    public static McpToolException Conflict(string message) => new(McpErrorCodes.Conflict, message);
}
