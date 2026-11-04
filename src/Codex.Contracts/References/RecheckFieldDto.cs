namespace Codex.Contracts.References;

// What a re-check found for one identifier. Outcome is ok, redirect or not_found.
public sealed record RecheckFieldDto(string Field, string Value, string Outcome, string? RedirectTo);
