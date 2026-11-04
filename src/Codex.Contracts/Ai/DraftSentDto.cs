namespace Codex.Contracts.Ai;

// GET /api/generations/{id}/sent: what a generation sent to the model, for "Show what was sent", read from the request
// stored when it ran. `Integration` is the label of the AI integration that answered (null when none was recorded);
// `Instruction` (the system text, empty for a user's own template) and `Prompt` are the text the provider was sent.
public sealed record DraftSentDto(
    string? Template,
    string Model,
    string? Integration,
    IReadOnlyList<string> Context,
    int? InputTokens,
    int? OutputTokens,
    string Instruction,
    string Prompt);
