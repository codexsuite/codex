namespace Codex.Contracts.Print;

// An act's or chapter's opener art. Kind is "act" or "chapter"; AssetId and Caption are null when it has none.
public sealed record OpenerDto(Guid Id, string Kind, Guid? AssetId, string? Caption);
