using Codex.Contracts.Errors;

namespace Codex.Application.Mcp;

// The `code` of a failed tool call: #20 section 2.3 plus the wave-3 decisions (M11). Where the HTTP API already has
// the code (ApiErrorCodes) the MCP code is the same string; its `validation` is called `invalid` here.
public static class McpErrorCodes
{
    public const string NotFound = ApiErrorCodes.NotFound;
    public const string Ambiguous = ApiErrorCodes.Ambiguous;
    public const string Conflict = ApiErrorCodes.Conflict;
    public const string OutOfScope = ApiErrorCodes.OutOfScope;
    public const string Blocked = ApiErrorCodes.Blocked;
    public const string NeedsConfirmation = ApiErrorCodes.NeedsConfirmation;
    public const string NotConfigured = ApiErrorCodes.NotConfigured;
    public const string Internal = ApiErrorCodes.Internal;
    public const string Invalid = "invalid";
    public const string RateLimited = "rate_limited";

    // #32: the token lacks the tool's scope (forbidden), or a stdio session's token stopped working (unauthorized).
    public const string Forbidden = ApiErrorCodes.Forbidden;
    public const string Unauthorized = ApiErrorCodes.Unauthorized;

    // A display number ("5.2") now names something other than what this session last read under that number.
    public const string StaleRef = "stale_ref";

    public static string FromApiCode(string apiCode) => apiCode == ApiErrorCodes.Validation ? Invalid : apiCode;
}
