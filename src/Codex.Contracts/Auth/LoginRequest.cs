namespace Codex.Contracts.Auth;

// POST /api/auth/login.
public sealed record LoginRequest(string? Email, string? Password);
