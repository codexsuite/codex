namespace Codex.Contracts.Structure;

// GET /api/chapters/{id}/archived-scenes. WasSceneNumber is the number the scene would take if restored now;
// ChapterNumber is null for an unnumbered chapter. Summary is Markdown.
public sealed record ArchivedSceneDto(
    Guid Id,
    int? ChapterNumber,
    int WasSceneNumber,
    string? Title,
    string? Summary,
    int Words,
    int UseCount);
