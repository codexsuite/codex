namespace Codex.Contracts.References;

// A source's cite key and permissions contact. Null leaves a field as it is; empty text clears it.
public sealed record SourceExtrasRequest(string? CiteKey, string? PermissionsContact);
