using Codex.Contracts.References;

namespace Codex.Application.Features.References;

// What merging a duplicate did (or, for a dry run, would do), with the kept source as it reads after. A dry run's Snapshot is
// the one a real merge would produce; nothing is written.
public sealed record SourceMergeResult(
    string DuplicateTitle,
    string KeptTitle,
    int UsesMoved,
    int ArchivedUsesMoved,
    IReadOnlyList<string> FilledFields,
    bool PermissionMoved,
    int FlagsMoved,
    SourceMergeSnapshot Snapshot,
    SourceDetailDto Kept);
