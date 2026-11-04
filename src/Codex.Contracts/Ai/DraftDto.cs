namespace Codex.Contracts.Ai;

// One AI generation as the draft panel shows it. `BeatId` is null for a "continue writing" draft, which sits at the end
// of the scene. `Template` is the template's display name as recorded when it was generated. `IntegrationId` is the AI
// integration that wrote it (null once that integration is removed), so Retry asks the same account.
public sealed record DraftDto(
    Guid Id,
    Guid SceneId,
    Guid? BeatId,
    string Kind,
    string Model,
    string? TemplateId,
    string? Template,
    int? TargetWords,
    int Attempt,
    string Status,
    string Text,
    int Words,
    Guid? RetryOfId,
    int? InputTokens,
    int? OutputTokens,
    Guid? IntegrationId);
