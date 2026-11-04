namespace Codex.Contracts.Ai;

public sealed record DraftStreamStarted(Guid GenerationId, int Attempt, string Model, string? TemplateId, string? Template, int? TargetWords);
