namespace Codex.Contracts.Auth;

// PATCH /api/admin/users/{id}: each field left null is unchanged. Unlock = true clears a lockout.
public sealed record UpdateUserRequest(bool? IsAdmin, bool? Disabled, bool? Unlock);
