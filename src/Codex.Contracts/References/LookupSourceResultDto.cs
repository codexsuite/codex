using Codex.Contracts.Sources;

namespace Codex.Contracts.References;

// Outcome is "existing" (the book already has it: ExistingId, ExistingTitle, ExistingByline), "found" (Source set),
// "redirect" (RedirectTo: the address moved) or "not_found". Value is the identifier in its canonical form
// (ISBN-13 digits, bare DOI, absolute URL), the one to save.
public sealed record LookupSourceResultDto(
    string Outcome,
    string Value,
    SourceMetadata? Source,
    Guid? ExistingId,
    string? ExistingTitle,
    string? ExistingByline,
    string? RedirectTo)
{
    public const string ExistingOutcome = "existing";
}
