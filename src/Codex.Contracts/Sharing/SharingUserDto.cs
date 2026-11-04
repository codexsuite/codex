namespace Codex.Contracts.Sharing;

// A person on the Sharing card. Email is shown to the book's owner only; for anyone else it is null, so sharing a
// book never hands one collaborator another's address.
public sealed record SharingUserDto(Guid Id, string DisplayName, string? Email);
