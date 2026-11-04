namespace Codex.Contracts.Auth;

// GET /api/auth/me (and the body of a successful login): who is signed in. Initials are what the bars' avatar shows:
// the first letters of the display name's first and last words ("Joel Dickson" -> "JD").
public sealed record MeDto(Guid Id, string Email, string DisplayName, string Initials, bool IsAdmin, bool MustChangePassword);
