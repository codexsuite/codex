namespace Codex.Contracts.Print;

// A live chapter in reading order. Number counts the numbered chapters from 1 (null for front matter).
public sealed record ManuscriptChapterDto(Guid Id, string Title, int? Number);
