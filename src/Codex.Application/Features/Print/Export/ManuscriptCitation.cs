namespace Codex.Application.Features.Print.Export;

// A live source use with the parts of its source a footnote or bibliography entry needs. Authors is the CSL names JSON.
public sealed record ManuscriptCitation(Guid UseId, Guid ReferenceId, string Authors, string Title, int? Year, string? Publisher, string? Locator);
