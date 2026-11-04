namespace Codex.Contracts.Manuscript;

public sealed record BeatDto(Guid Id, Guid SceneId, string Body, int? TargetWordCount, bool IsHidden, IReadOnlyList<Guid> ContextEntryIds, string Title);
