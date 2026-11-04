namespace Codex.Contracts.References;

// Field is isbn, doi or url; Status is ok, redirect, not_found, error or checking. ResolvedUrl is where a redirect lands.
public sealed record IdentifierCheckDto(string Field, string Status, string? ResolvedUrl);
