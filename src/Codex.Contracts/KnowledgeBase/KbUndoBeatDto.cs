namespace Codex.Contracts.KnowledgeBase;

// A beat body as it was before the delete.
public sealed record KbUndoBeatDto(Guid BeatId, string Body);
