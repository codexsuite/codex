namespace Codex.Application.Features.Print.Render;

// Everything the assembler reads, loaded once by ManuscriptReader: the book, its acts and chapters in reading order,
// the scenes in scope with their prose, and what their directives and links point at.
public sealed record ManuscriptSnapshot(
    RenderBook Book,
    RenderScope Scope,
    IReadOnlyList<RenderAct> Acts,
    IReadOnlyList<RenderChapter> Chapters,
    IReadOnlyList<RenderScene> Scenes,
    IReadOnlyDictionary<Guid, RenderFigure> Figures,
    IReadOnlyDictionary<Guid, RenderImage> Images,
    IReadOnlyDictionary<Guid, RenderEntry> Entries,
    IReadOnlyDictionary<Guid, RenderCitation> Citations);
