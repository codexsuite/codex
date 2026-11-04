namespace Codex.Application.Features.Import;

// The names of the mapper's rules, as they key LatexImportResult.Mapped.
public static class LatexImportRule
{
    public const string ReadAloud = "readaloud";
    public const string Sidebar = "sidebar";
    public const string Aside = "aside";
    public const string PageBreak = "pagebreak";
    public const string ColumnDropped = "column-dropped";
    public const string DropCap = "dropcap";
    public const string Spell = "spell";
    public const string HeadingNumber = "heading-number";
    public const string FigureFenced = "figure-fenced";
    public const string BlockFenced = "block-fenced";
    public const string SpanFenced = "span-fenced";
}
