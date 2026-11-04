namespace Codex.Contracts.Books;

// Who owns a book, for the Library's "Shared by …" line and MCP's list_books (#80).
public sealed record BookOwnerDto(Guid Id, string DisplayName);
