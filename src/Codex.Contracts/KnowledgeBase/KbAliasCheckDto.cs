namespace Codex.Contracts.KnowledgeBase;

// What a proposed alias would do: CollidesWith is the name of another entry in scope that already answers to it (null
// for none); Matches is how many whole-word, case-insensitive uses it has in the scope's prose; Warn is true when it is
// short (3 letters or fewer) or common (25 or more matches), so it would underline a lot.
public sealed record KbAliasCheckDto(string? CollidesWith, int Matches, bool Warn);
