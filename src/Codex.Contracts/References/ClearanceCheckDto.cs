namespace Codex.Contracts.References;

// One line of the checklist. Key is words, lyrics, figures, data, licence, citation or identifiers; Status is ok, warn
// or danger; Detail is the server's wording of the finding.
public sealed record ClearanceCheckDto(string Key, string Status, string Title, string Detail);
