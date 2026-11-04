namespace Codex.Contracts.Manuscript;

public sealed record BookBeatsDto(Guid BookId, IReadOnlyList<BeatDto> Beats);
