namespace Codex.Application.Identity;

// What a request needs to know about its user: whether the session still counts (not disabled, same security stamp)
// and whether the user is an admin.
public sealed record UserSnapshot(
    Guid Id, string Email, string DisplayName, bool IsAdmin, bool MustChangePassword, bool Disabled, Guid SecurityStamp);
