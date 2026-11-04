namespace Codex.Contracts;

// The access rule (#80, extending #29's ownership rule): a book (or series), and everything inside it, may be opened
// by its owner (book.owner_id) and by the users it is shared with, each at a role (book_access). Every command and
// query that takes a book id, or the id of anything inside a book, resolves it through here first and states the
// minimum role it needs, so the API and the MCP tools (which call the same commands and queries) share one check.
// No access at all throws ApiException.NotFound, exactly as an id that does not exist does; access below the minimum
// throws ApiException.Forbidden.
public interface IBookAccess
{
    // Throws not_found unless the current user can open bookId (a book or a series), forbidden when their role there
    // is below minimum; returns their effective role. notFound replaces the default message ("Book <id> was not
    // found."), so a command keeps the wording it used for an id that does not exist.
    Task<BookRole> RequireAsync(
        Guid bookId, BookRole minimum, CancellationToken cancellationToken = default, string? notFound = null);

    // The same for an item inside a book: throws not_found unless the item exists and sits in a book or series the
    // current user can open, forbidden below minimum; returns that book's id (null only for an asset or import that
    // belongs to the user but to no book, which only its owner reaches).
    Task<Guid?> RequireAsync(
        BookItem item, Guid id, BookRole minimum, CancellationToken cancellationToken = default, string? notFound = null);

    // The current user's effective role on bookId, or null when they cannot open it. Never throws for access.
    Task<BookRole?> RoleAsync(Guid bookId, CancellationToken cancellationToken = default);
}
