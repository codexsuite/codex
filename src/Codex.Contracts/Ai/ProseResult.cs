namespace Codex.Contracts.Ai;

// The totals for a finished (or failed-after-output) draft: the model that answered and the tokens used.
public sealed record ProseResult(string Model, int InputTokens, int OutputTokens);
