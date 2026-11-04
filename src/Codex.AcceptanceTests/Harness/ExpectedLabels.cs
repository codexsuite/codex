using Codex.Core;

namespace Codex.AcceptanceTests.Harness;

// Expected relative-time labels, computed the way the server computes them (time is never frozen, D4).
// Use for timestamps the test itself created in the run, e.g. "just now" after an edit.
public static class ExpectedLabels
{
    public static string Relative(DateTimeOffset value) => DisplayDate.Relative(value, TimeProvider.System);

    public static string JustNow() => Relative(TimeProvider.System.GetUtcNow());
}
