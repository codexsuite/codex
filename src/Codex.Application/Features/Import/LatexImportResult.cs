namespace Codex.Application.Features.Import;

// The mapper's output: the canonical prose, how many commands each rule mapped (by rule name, see LatexImportRule),
// and every fenced block or span, for the import's skipped/flagged list.
public sealed record LatexImportResult(string Prose, IReadOnlyDictionary<string, int> Mapped, IReadOnlyList<LatexImportFlag> Fenced);
