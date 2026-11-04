namespace Codex.Contracts.Auth;

// POST /api/admin/users.
public sealed record CreateUserRequest(string? Email, string? DisplayName);
