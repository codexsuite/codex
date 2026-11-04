namespace Codex.Contracts.KnowledgeBase;

// An entry linked to this one (both ways).
public sealed record KbRelatedDto(Guid Id, string Name, string CategoryName);
