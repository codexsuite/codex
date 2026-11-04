namespace Codex.Contracts.References;

// POST /api/books/{id}/references/lookup. Kind is isbn, doi or url (SourceLookupKinds); Value is what the writer pasted.
public sealed record LookupSourceRequest(string Kind, string Value);
