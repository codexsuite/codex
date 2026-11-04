namespace Codex.Application.Prose;

// What a piece of text is, for Find and replace: scene prose (blocks, directives, fences and links), a scene summary
// (one paragraph with mention links) or plain text (a beat's instruction).
public enum SearchKind
{
    Prose,
    Summary,
    Plain,
}
