namespace Codex.Application.Prose;

// One embed found in a body. Start and Length are character offsets into the body. Id is the beat, addition or
// figure id, the Knowledge base entry id (mentions, spells, stat blocks), the chapter id or the reference use id.
// Text is the visible link text for inline embeds and null for directives. A bare reference marker has the text "^".
public sealed record ProseEmbed(ProseEmbedKind Kind, Guid Id, int Start, int Length, string? Text);
