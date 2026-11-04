namespace Codex.Contracts.Books;

// One row of book_scope for the book: the book itself (Kind "book") or its series (Kind "series").
public sealed record BookScopeEntryDto(Guid Id, string Kind, string Title);
