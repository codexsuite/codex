namespace Codex.Contracts.References;

// The knowledge base entry a source is linked to (the "Codex" link).
public sealed record SourceEntryDto(Guid Id, string Name, Guid CategoryId);
