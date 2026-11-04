namespace Codex.Contracts.Events;

// The outline changed. Kind is one of act, chapter, scene, beat, label, kb.
public sealed record StructureChanged(Guid BookId, string Kind) : BookEvent(BookId);
