namespace Codex.Contracts.Ai.Integrations;

// One of the signed-in user's AI integrations (Settings → AI). The key itself never leaves the server: KeyHint is
// "sk-…abcd" (the key's prefix up to its first '-', if any, and its last 4 characters). Models is the provider's list
// from the last Test connection (empty: none fetched, the author types a model id). LastUsedLabel is null for an
// integration never used; LastError is the last draft or test failure's message, cleared by the next success.
public sealed record AiIntegrationDto(
    Guid Id,
    string Kind,
    string Label,
    string? BaseUrl,
    string KeyHint,
    string? DefaultModel,
    IReadOnlyList<string> Models,
    bool IsDefault,
    string CreatedLabel,
    string? LastUsedLabel,
    string? LastError);
