namespace Codex.Contracts.Auth;

// POST /api/auth/device-sessions: the mobile app's sign-in (#35). The same email and password checks as LoginRequest;
// DeviceName is what the Account page lists it as ("JD's iPhone").
public sealed record CreateDeviceSessionRequest(string? Email, string? Password, string? DeviceName);
