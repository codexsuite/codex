namespace Codex.Contracts.Auth;

// POST /api/auth/tokens. Scopes are codex:* values; codex:read is always granted, so it may be left out.
// ExpiresInDays null means the token never expires (the page offers 90 days by default).
public sealed record CreateApiTokenRequest(string? Name, IReadOnlyList<string>? Scopes, int? ExpiresInDays);
