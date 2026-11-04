namespace Codex.Contracts.References;

// One parsed entry. Provider is bibtex, ris or csl (CSL JSON, what Zotero exports); Key is the BibTeX key, the RIS ID
// or the CSL id (null when the file gave none).
public sealed record ImportSourceItem(string Provider, string? Key, SourceDetailsRequest Details);
