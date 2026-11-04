namespace Codex.Contracts.Structure;

// The chapter fields PATCH /api/chapters/{id} writes, as they read now (the outline carries only title and number).
public sealed record ChapterFieldsDto(string Title, string Summary, string Notes, bool IsNumbered);
