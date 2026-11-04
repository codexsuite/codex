namespace Codex.Contracts.Settings;

// One cited source against the quote limit. Reason is quote_over_limit, lyric or figure when the source needs
// permission, else null; AtRisk is true when it needs permission that isn't granted. Why is the server's wording.
public sealed record RuleSourceDto(
    Guid ReferenceId,
    string Title,
    int? Number,
    int QuotedWords,
    string? Reason,
    bool AtRisk,
    string? Why);
