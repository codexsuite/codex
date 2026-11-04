namespace Codex.Contracts.Library;

// Absent = unchanged. A name must not be blank; an empty description clears it.
public sealed record PatchSeriesRequest(string? Name, string? Description);
