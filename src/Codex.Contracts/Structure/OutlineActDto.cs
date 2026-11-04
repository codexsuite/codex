namespace Codex.Contracts.Structure;

// Opener is the act's part-opener art, null when it has none.
public sealed record OutlineActDto(
    Guid Id, string Title, string Position, IReadOnlyList<OutlineChapterDto> Chapters, OutlineOpenerDto? Opener);
