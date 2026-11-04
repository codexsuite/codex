namespace Codex.Contracts.Library;

// What deleting a book removes: its live chapters and scenes, words and sources.
public sealed record BookDeleteImpactDto(Guid BookId, string Title, bool Archived, int Chapters, int Scenes, int Words, int Sources);
