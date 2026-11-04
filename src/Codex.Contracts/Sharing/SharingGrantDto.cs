namespace Codex.Contracts.Sharing;

// One grant: who, and at which role ("view", "comment" or "write"; BookRoles wire values).
public sealed record SharingGrantDto(SharingUserDto User, string Role);
