namespace Codex.Application.Features.Import;

// What PandocChapterReader found in a chapter file (docs/import.md, pandoc).
public enum PandocBlockKind
{
    // \part{Title}, from a full-page figure (Image, Caption) or on its own line.
    Part,

    // A level-1 heading: Title, hand numbering stripped.
    Chapter,

    // Author Markdown for ImportProse.ToStored: deeper headings moved up a level and unnumbered, drop caps undone.
    Text,

    // LaTeX nothing maps: Text holds it, for a ```{=latex} fence.
    Latex,

    // A pipe table: Text holds its rows.
    Table,

    // An HTML comment: Text holds it, kept but never printed.
    Comment,

    // \newpage or \clearpage.
    Break,

    // ![Caption](Image) or a figure environment with one \includegraphics; Placement is a figure.placement value.
    Figure,

    // \DndSpellHeader: Title, Fields (level-school, casting-time, range, components, duration) and Children (its text).
    Spell,

    // DndReadAloud, DndSidebar or DndComment: Container (readaloud, sidebar or aside), Title, Children.
    Container,
}
