namespace Codex.Contracts.Import;

// A finished import (book_import.result), for the Plan banner and the dialog's last step. Counts are what was
// created; Words is the imported prose's word count as the database counts it. Sources, CitedSources and Labels are
// only filled by a Codex backup (the other sources have none).
public sealed record ImportSummaryDto(
    Guid ImportId,
    Guid BookId,
    string Source,
    string FileName,
    int Acts,
    int Chapters,
    int Scenes,
    int Words,
    int Entries,
    IReadOnlyList<ImportSkippedDto> Skipped,
    int Sources = 0,
    int CitedSources = 0,
    int Labels = 0);
