namespace Codex.Contracts;

// The kinds of thing IBookAccess can check: each resolves to the book (or series) it sits in.
public enum BookItem
{
    Book,
    Act,
    Chapter,
    Scene,
    Beat,
    Figure,
    KbCategory,
    KbEntry,
    KbAddition,
    Label,
    Source,
    ReferenceUse,
    QuoteFlag,
    PermissionRequest,
    Asset,
    Generation,
    Import,
    ExportJob,
}
