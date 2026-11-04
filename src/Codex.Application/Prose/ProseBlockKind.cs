namespace Codex.Application.Prose;

public enum ProseBlockKind
{
    Paragraph,
    Heading,
    Quote,
    List,
    Directive,
    ContainerMarker,
    Fence,

    // A GFM pipe table (#64): header row, delimiter row, body rows.
    Table,

    // `---`, `***` or `___` alone as a block (#64).
    Rule,

    // A `::` line that is not a known directive, or an unknown, unclosed or stray `:::` container, or an HTML comment: kept as it is.
    Opaque,
}
