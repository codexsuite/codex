namespace Codex.Contracts.References;

// Skipped entries were duplicates of a source already in the book (or earlier in the file) or had no title.
// SkippedReasons runs parallel to SkippedTitles ("already in References" or "no title"); Duplicates and Untitled count
// the two reasons. The preview answers with the same shape: what an import would do, written nowhere.
public sealed record ImportSourcesResult(
    int Created,
    int Skipped,
    IReadOnlyList<string> SkippedTitles,
    int Duplicates = 0,
    int Untitled = 0,
    IReadOnlyList<string>? SkippedReasons = null);
