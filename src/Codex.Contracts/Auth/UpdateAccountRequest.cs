namespace Codex.Contracts.Auth;

// PATCH /api/auth/me: change your own display name.
public sealed record UpdateAccountRequest(string? DisplayName);
