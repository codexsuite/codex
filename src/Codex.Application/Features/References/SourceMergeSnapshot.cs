namespace Codex.Application.Features.References;

// What a merge changed, enough to put it back: the duplicate's uses (each with its changed_at before and after, since a moved
// live use counts as new for the kept source), the kept source's gap-fill columns as JSON before and after with the keys that
// were filled, the permission request and its letters that moved, and the quote flags that were repointed. An unmerge
// (SourceMergeCommands.UnmergeAsync) runs only while all of it still reads what the merge left.
public sealed record SourceMergeSnapshot(
    Guid DuplicateId,
    Guid KeptId,
    IReadOnlyList<SourceMergeSnapshot.MovedUse> Uses,
    string Before,
    string After,
    IReadOnlyList<string> Filled,
    Guid? PermissionId,
    string? PermissionStatus,
    IReadOnlyList<Guid> Attachments,
    IReadOnlyList<Guid> Flags)
{
    public sealed record MovedUse(Guid Id, bool Live, DateTimeOffset OldChangedAt, DateTimeOffset NewChangedAt);
}
