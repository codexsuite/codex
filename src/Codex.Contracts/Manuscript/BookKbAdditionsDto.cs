namespace Codex.Contracts.Manuscript;

public sealed record BookKbAdditionsDto(Guid BookId, IReadOnlyList<KbAdditionDto> Additions);
