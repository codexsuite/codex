namespace Codex.Application.Features.Import;

// One piece of LaTeX the mapper could not map and kept as raw LaTeX: Kind is "block" (a {=latex} fence) or "span" (an
// inline {=latex} span); Line is 1-based in the source text.
public sealed record LatexImportFlag(int Line, string Kind, string Command, string Excerpt);
