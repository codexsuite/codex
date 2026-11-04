namespace Codex.Application.Prose;

public sealed record ReplaceOutcome(string Text, int Replaced, int Cited, int Spans);
