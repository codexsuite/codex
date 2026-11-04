namespace Codex.Contracts.Auth;

// POST /api/auth/device-sessions: the new session's row, its bearer secret (cdx_dev_…), which the server keeps only as
// a hash and never shows again, and the signed-in user.
public sealed record CreatedDeviceSessionDto(DeviceSessionDto Session, string Secret, MeDto Me);
