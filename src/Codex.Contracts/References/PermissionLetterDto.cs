namespace Codex.Contracts.References;

// The signed letter kept with a granted request. Url downloads it (GET /api/permission-requests/{id}/letter).
public sealed record PermissionLetterDto(string FileName, string Url);
