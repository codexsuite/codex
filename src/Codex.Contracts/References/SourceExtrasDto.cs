namespace Codex.Contracts.References;

// A source's cite key (BibTeX key, unique in the book) and where a permission request goes; null when not set.
public sealed record SourceExtrasDto(string? CiteKey, string? PermissionsContact);
