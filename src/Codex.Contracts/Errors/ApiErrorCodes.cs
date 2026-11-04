namespace Codex.Contracts.Errors;

// The machine-readable error codes. The first six are #20 section 2.3 verbatim; `validation` is the
// request-shape failure (400) that #20 leaves to the transport, `not_configured` (503) is a feature whose
// provider has not been set up, `unauthorized` (401) is a request with no signed-in user (or a failed sign-in),
// `forbidden` (403) a signed-in user without the right (an admin page, a cross-site write), and `internal_error` (500)
// is what any unhandled exception becomes, with a fixed message that never leaks details. `rate_limited` (429) is a
// caller over a rate limit (the sharing lookup, the per-IP sign-in limiter).
public static class ApiErrorCodes
{
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
    public const string Conflict = "conflict";
    public const string OutOfScope = "out_of_scope";
    public const string Blocked = "blocked";
    public const string NeedsConfirmation = "needs_confirmation";
    public const string Validation = "validation";
    public const string NotConfigured = "not_configured";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string RateLimited = "rate_limited";
    public const string Internal = "internal_error";

    public static IReadOnlyList<string> All { get; } =
        [NotFound, Ambiguous, Conflict, OutOfScope, Blocked, NeedsConfirmation, Validation, NotConfigured, Unauthorized, Forbidden, RateLimited, Internal];

    // not_found 404; validation 400; out_of_scope and blocked 422 (understood, refused);
    // conflict, ambiguous and needs_confirmation 409 (state must change or the caller must decide).
    public static int StatusFor(string code) => code switch
    {
        NotFound => 404,
        Validation => 400,
        Unauthorized => 401,
        Forbidden => 403,
        RateLimited => 429,
        Internal => 500,
        NotConfigured => 503,
        OutOfScope or Blocked => 422,
        Conflict or Ambiguous or NeedsConfirmation => 409,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown API error code."),
    };
}
