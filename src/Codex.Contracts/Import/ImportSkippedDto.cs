namespace Codex.Contracts.Import;

// A file (or part of one) the importer leaves out, with the reason shown in warn.
public sealed record ImportSkippedDto(string Path, string Reason);
