using Codex.Contracts.Structure;

namespace Codex.Application.Mcp;

// A book's outline (OutlineQuery's OutlineDto) indexed the way ItemRefs and labels need it: live acts numbered in
// order ("act:2"), live chapters by number and slug, live scenes by display number ("5.2"). Archived chapters are kept
// apart. Previous is the number → id map this session saw the last time it read the book (null on the first read),
// for stale_ref.
public sealed class BookOutline
{
    public BookOutline(McpBook book, OutlineDto dto, IReadOnlyDictionary<string, Guid>? previous)
    {
        Book = book;
        Dto = dto;
        Previous = previous;
        Acts = dto.Acts.Select((act, i) => new Act(i + 1, act)).ToList();
        Chapters = Acts.SelectMany(a => a.Dto.Chapters.Where(c => !c.Archived).Select(c => new Chapter(a, c))).ToList();
        ArchivedChapters = Acts.SelectMany(a => a.Dto.Chapters.Where(c => c.Archived).Select(c => new Chapter(a, c))).ToList();
        Scenes = Chapters.SelectMany(c => c.Dto.Scenes.Select(s => new Scene(c, s))).ToList();
    }

    public McpBook Book { get; }

    public OutlineDto Dto { get; }

    public IReadOnlyDictionary<string, Guid>? Previous { get; }

    public IReadOnlyList<Act> Acts { get; }

    public IReadOnlyList<Chapter> Chapters { get; }

    public IReadOnlyList<Chapter> ArchivedChapters { get; }

    public IReadOnlyList<Scene> Scenes { get; }

    // The display numbers a ref can use, for McpRefMemory: "act:2", "5", "5.2" (scenes in an unnumbered chapter
    // have no number of their own that a ref could use).
    public IReadOnlyDictionary<string, Guid> Numbers() =>
        Acts.Select(a => (a.Number, a.Dto.Id))
            .Concat(Chapters.Where(c => c.Dto.Number != null).Select(c => (c.Number!, c.Dto.Id)))
            .Concat(Scenes.Where(s => s.Chapter.Dto.Number != null).Select(s => (s.Dto.Number, s.Dto.Id)))
            .ToDictionary(p => p.Item1, p => p.Item2, StringComparer.Ordinal);

    public Scene? SceneById(Guid id) => Scenes.FirstOrDefault(s => s.Dto.Id == id);

    // The act, chapter or scene with this id, as an item; null when the outline has no live one.
    public McpItem? ItemById(Guid id) =>
        Acts.FirstOrDefault(a => a.Dto.Id == id)?.Item(Book.Id)
        ?? Chapters.FirstOrDefault(c => c.Dto.Id == id)?.Item(Book.Id)
        ?? SceneById(id)?.Item(Book.Id);

    public sealed record Act(int Index, OutlineActDto Dto)
    {
        public string Number => $"act:{Index}";

        public string Label => McpLabels.Act(Index, Dto.Title);

        public McpItem Item(Guid bookId) => new(Dto.Id, "act", Label, CodexLinks.Plan(bookId), Number);
    }

    public sealed record Chapter(Act Act, OutlineChapterDto Dto)
    {
        public string? Number => Dto.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string Label => McpLabels.Chapter(Dto.Number, Dto.Title);

        public McpItem Item(Guid bookId) => new(Dto.Id, "chapter", Label, CodexLinks.Plan(bookId, chapter: Number), Number ?? $"ch:{Dto.Slug}");
    }

    public sealed record Scene(Chapter Chapter, OutlineSceneDto Dto)
    {
        public string Label => McpLabels.Scene(Dto.Number, Chapter.Dto.Title, Dto.Title, Dto.Summary);

        // Scenes link into Write, where they are read and drafted.
        public McpItem Item(Guid bookId) =>
            new(Dto.Id, "scene", Label, CodexLinks.Write(bookId, scene: Chapter.Dto.Number == null ? null : Dto.Number), Dto.Number);
    }
}
