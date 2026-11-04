namespace Codex.Application.Features.Import;

// Turns Markdown that holds raw LaTeX (pandoc + the rpgtex dndbook class, #13/#64) into the stored prose format
// (docs/prose-format.md): the known environments become containers, page breaks become ::pagebreak, drop caps and
// hand-numbered headings are dropped, and everything else LaTeX is kept as raw LaTeX (a {=latex} fence or span), so
// the output never holds unfenced LaTeX. Pure: no IO. HTML comments and pipe tables pass through byte for byte; the
// rest of the text goes through ImportProse.ToStored.
public static class LatexImportMapper
{
    // spellEntryId maps a \DndSpellHeader's spell name to a knowledge base entry id; null (or a null answer) fences it.
    public static LatexImportResult Map(string markdown, Func<string, string?>? spellEntryId = null) =>
        new LatexImportRun(spellEntryId).Run(markdown);
}
