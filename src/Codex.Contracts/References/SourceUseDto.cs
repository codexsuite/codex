namespace Codex.Contracts.References;

// One passage that cites the source, in reading order. Kind is quote, paraphrase, data, figure or lyric.
// NewSinceSignOff marks a use added or changed after the source was signed off. Pending is true while the marker
// exists only in an AI draft.
public sealed record SourceUseDto(
    Guid Id,
    Guid SceneId,
    int? ChapterNumber,
    string ChapterTitle,
    int SceneNumber,
    string Kind,
    string Excerpt,
    int Words,
    string? Locator,
    bool NewSinceSignOff,
    bool Pending);
