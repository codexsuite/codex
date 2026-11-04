namespace Codex.Contracts.Auth;

// POST /api/auth/password: change your own password.
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
