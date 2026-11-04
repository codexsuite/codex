namespace Codex.Contracts.KnowledgeBase;

// The References source linked to an entry. Status is the clearance status ("cleared", "needs_check", "risk",
// "not_cited") and Reason its code, both worked out by the database.
public sealed record KbSourceDto(Guid Id, string Title, string Status, string Reason);
