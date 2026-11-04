namespace Codex.TestSupport.Contracts;

// What a generator under test is told to do: stream `Chunks`, hold after the first until `Gate` completes,
// and throw `FailWith` once the chunks are out. A real provider's fixture maps this onto a stubbed backend.
public sealed record ProseScript(IReadOnlyList<string> Chunks, Task? Gate = null, Exception? FailWith = null);
