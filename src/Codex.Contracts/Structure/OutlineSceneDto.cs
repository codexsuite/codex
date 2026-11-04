namespace Codex.Contracts.Structure;

// Number is "<chapter number>.<scene number>" ("5.2"), or just the scene number in an unnumbered chapter.
// Summary is Markdown. Status is the stored value ("outline", "draft", ...). HasPendingDraft: an AI generation
// for this scene is waiting to be applied or discarded.
public sealed record OutlineSceneDto(
    Guid Id,
    string Number,
    string? Title,
    string? Summary,
    string Status,
    string Position,
    int Words,
    bool HasPendingDraft,
    IReadOnlyList<LabelRefDto> Labels,
    IReadOnlyList<EntryRefDto> LinkedEntries);
