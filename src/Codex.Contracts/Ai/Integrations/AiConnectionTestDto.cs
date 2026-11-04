namespace Codex.Contracts.Ai.Integrations;

// The answer to Test connection. Ok with the provider's model ids (sorted; may be empty when the provider has no
// /models list), or not Ok with a fixed message saying what failed ("the key was rejected", …). A validation problem
// with the request itself (a refused base URL) is an ordinary 400, not a failed test.
public sealed record AiConnectionTestDto(bool Ok, IReadOnlyList<string> Models, string? Error);
