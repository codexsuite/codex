using Codex.Contracts.Ai;

namespace Codex.Application.Features.Ai;

// A generation row as selected by DraftQueries.Columns.
internal sealed record DraftRow(
    Guid Id,
    Guid SceneId,
    Guid? BeatId,
    string Kind,
    string Model,
    string? TemplateId,
    string? Template,
    int? TargetWords,
    int? Attempt,
    string Status,
    string? Text,
    int Words,
    Guid? RetryOfId,
    int? InputTokens,
    int? OutputTokens,
    Guid? IntegrationId)
{
    public DraftDto ToDto() => new(Id, SceneId, BeatId, Kind, Model, TemplateId, Template, TargetWords, Attempt ?? 1, Status, Text ?? "", Words, RetryOfId, InputTokens, OutputTokens, IntegrationId);
}
