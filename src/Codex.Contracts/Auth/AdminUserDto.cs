namespace Codex.Contracts.Auth;

// One row of the user management page (GET /api/admin/users). Status is "active", "disabled" or "locked".
// LastLoginLabel is null when the user has never signed in. TokenCount: their personal access tokens that still work
// (not revoked, not expired).
public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsAdmin,
    string Status,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    string? LastLoginLabel,
    DateTimeOffset CreatedAt,
    string CreatedLabel,
    int TokenCount);
