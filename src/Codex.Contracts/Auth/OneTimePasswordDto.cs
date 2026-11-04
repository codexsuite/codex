namespace Codex.Contracts.Auth;

// POST /api/admin/users and POST /api/admin/users/{id}/reset-password: the user row and the one-time password,
// which the server never stores or shows again. The admin passes it on; the user must change it at first sign-in.
public sealed record OneTimePasswordDto(AdminUserDto User, string OneTimePassword);
