namespace Codex.Contracts;

// What a user may do in a book (#80), lowest to highest. View reads everything; Comment adds comments (#26); Write
// changes content and settings; Owner (book.owner_id, never a grant row) alone manages sharing and the book's
// lifecycle. A role travels on the wire as its lower-case name ("view", "comment", "write", "owner"), the value
// book_access.role holds.
public enum BookRole
{
    View = 1,
    Comment = 2,
    Write = 3,
    Owner = 4,
}
