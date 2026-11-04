namespace Codex.Application.Prose;

public sealed record SearchOptions(string Query, bool MatchCase = false, bool WholeWord = false);
