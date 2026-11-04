namespace Codex.Contracts.Structure;

// Number is the continuous chapter number across acts; null for an unnumbered chapter (Preface) or an archived one.
// Opener is the chapter's opener art, null when it has none.
public sealed record OutlineChapterDto(
    Guid Id,
    int? Number,
    string Title,
    string Slug,
    string Position,
    bool Archived,
    int ArchivedSceneCount,
    IReadOnlyList<OutlineSceneDto> Scenes,
    OutlineOpenerDto? Opener);
