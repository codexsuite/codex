namespace Codex.Contracts.KnowledgeBase;

// One live scene that uses the entry. Number is "5.2" (or the scene number in an unnumbered chapter);
// ChapterNumber is null for an unnumbered chapter. Snippet is the plain-text sentence that shows the entry in use.
public sealed record KbUseDto(
    Guid SceneId,
    Guid BookId,
    int? ChapterNumber,
    string ChapterTitle,
    string Number,
    IReadOnlyList<KbUseKindDto> Kinds,
    string Snippet);
