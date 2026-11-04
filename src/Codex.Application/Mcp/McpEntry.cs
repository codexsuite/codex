namespace Codex.Application.Mcp;

// A resolved EntryRef: the entry, the book or series that owns it, and the live book its links open in (the book the
// ref was resolved in, else the most recently edited live book of the owner's scope). Null only for a series with no
// live book, whose entries then link to the series on the Library shelf.
public sealed record McpEntry(Guid Id, string Name, Guid OwnerId, Guid? LinkBookId)
{
    public McpItem Item => new(Id, "kb_entry", Name, EntryUrl(Id));

    // Plan with an entry's window open (entry=), for this entry or one related to it (same scope).
    public string EntryUrl(Guid entryId) => LinkBookId is { } book ? CodexLinks.Plan(book, entry: entryId) : CodexLinks.Series(OwnerId);

    // Plan with the entry highlighted (kb=).
    public string HighlightUrl(Guid entryId) => LinkBookId is { } book ? CodexLinks.Plan(book, kb: entryId) : CodexLinks.Series(OwnerId);
}
