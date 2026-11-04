namespace Codex.Contracts.Citations;

// POST /api/citations: cite a source for words already in a scene's prose. Passage is the stored Markdown of the
// words (one paragraph-like block, no link inside it); Occurrence picks the n-th match in the body (1-based, default
// the first one a reference can mark). Version is the scene's prose version the passage was read at. Kind is quote,
// paraphrase (the default), data, figure or lyric; Locator is where in the source ("pp. 41-43", "Table 5.1").
public sealed record CitePassageRequest(
    Guid SceneId,
    Guid SourceId,
    int Version,
    string Passage,
    int? Occurrence = null,
    string? Kind = null,
    string? Locator = null);
