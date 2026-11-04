namespace Codex.Contracts.Settings;

// GET /api/books/{id}/settings/rules-impact?limit=: what a quote limit (default: the book's own) would do to References.
// Nothing is written. Sources are the cited ones by marker number; RiskCount is how many are at risk (they block print export).
public sealed record RulesImpactDto(int Limit, int RiskCount, IReadOnlyList<RuleSourceDto> Sources);
