namespace Codex.Application.Features.Print.Render;

// How a sandboxed run ended. ExitCode 125 is the sandbox refusing to start (no Landlock, a bad path).
public sealed record ProcessOutcome(int ExitCode, string Stdout, string Stderr, bool TimedOut, TimeSpan Duration);
