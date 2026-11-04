namespace Codex.Contracts.Ai;

public sealed record DraftStreamFinal(Guid GenerationId, string Model, int InputTokens, int OutputTokens, int Words);
