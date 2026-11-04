namespace Codex.Contracts.Auth;

// One signed-in device on the Account page's Signed-in devices list (GET /api/auth/device-sessions, #35): a bearer
// session the mobile app signed in with. Prefix is "cdx_dev_" and the first 8 characters after it. Current marks the
// session the request itself was made with. ExpiresAt slides 90 days on with each use. LastUsedLabel is null for a
// session never used after sign-in.
public sealed record DeviceSessionDto(
    Guid Id,
    string Name,
    string Prefix,
    bool Current,
    DateTimeOffset ExpiresAt,
    string ExpiresLabel,
    DateTimeOffset? LastUsedAt,
    string? LastUsedLabel,
    DateTimeOffset CreatedAt,
    string CreatedLabel);
