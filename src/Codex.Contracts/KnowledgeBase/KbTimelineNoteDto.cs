namespace Codex.Contracts.KnowledgeBase;

// A timeline note (Codex Addition): a fact that holds from the start of its scene onward. Body is Markdown.
public sealed record KbTimelineNoteDto(Guid Id, Guid SceneId, Guid BookId, string SceneNumber, string Body);
