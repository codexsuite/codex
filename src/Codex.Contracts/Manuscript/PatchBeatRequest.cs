namespace Codex.Contracts.Manuscript;

// Null leaves a field as it is. TargetWordCount must be positive. An empty Title clears it.
public sealed record PatchBeatRequest(string? Body, bool? IsHidden, int? TargetWordCount, string? Title = null);
