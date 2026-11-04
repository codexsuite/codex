namespace Codex.Contracts.Manuscript;

// A Codex addition that is in a scene's prose: what holds for the entry from that point in the story on.
public sealed record KbAdditionDto(Guid Id, Guid SceneId, Guid EntryId, string Body);
