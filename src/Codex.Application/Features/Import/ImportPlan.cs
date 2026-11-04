using Codex.Contracts.Print;

namespace Codex.Application.Features.Import;

// Everything an import will create, in reading order. FileName is what book_import records (the folder or file name).
// Prose bodies in ScenePlan are already in the stored format, without the beat lines (the writer adds those). A Codex
// backup (Backup set) keeps its beat lines in the prose and is written by BackupWriter instead.
// Categories describe the fields of categories the entries use; Layout is the template choice made once the book exists.
public sealed record ImportPlan(
    string FileName,
    int FileCount,
    ImportBookPlan Book,
    IReadOnlyList<ImportActPlan> Acts,
    IReadOnlyList<ImportEntryPlan> Entries,
    IReadOnlyList<ImportSkippedItem> Skipped,
    IReadOnlyList<ImportCategoryPlan>? Categories = null,
    LayoutSelectionRequest? Layout = null,
    BackupData? Backup = null)
{
    // Every image the plan places (act openers, then figures), each once, in reading order.
    public IReadOnlyList<string> ImagePaths() =>
        Acts.SelectMany(a => (a.Opener == null ? Array.Empty<string>() : [a.Opener.ImagePath])
                .Concat(a.Chapters.SelectMany(c => c.Scenes).SelectMany(s => s.Figures ?? []).Select(f => f.ImagePath)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
