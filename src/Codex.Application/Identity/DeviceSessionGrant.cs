namespace Codex.Application.Identity;

// The device session behind a bearer secret: which session, and whose.
public sealed record DeviceSessionGrant(Guid SessionId, Guid UserId);
