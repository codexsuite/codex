namespace Codex.Contracts.Auth;

// One personal access token on the Account page's Agent access list (GET /api/auth/tokens, #32). Prefix is
// "cdx_pat_" and the first 8 characters after it; the rest of the secret is never shown again. Scopes are codex:*
// values. ExpiresLabel is null for a token that never expires, LastUsedLabel for one never used.
public sealed record ApiTokenDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    string? ExpiresLabel,
    bool Expired,
    DateTimeOffset? LastUsedAt,
    string? LastUsedLabel,
    DateTimeOffset CreatedAt,
    string CreatedLabel);
