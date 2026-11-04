namespace Codex.Contracts.Ai.Integrations;

// One integration in the Write draft panel's model picker: its models (empty: the author types one) and its default.
public sealed record AiModelChoiceDto(Guid IntegrationId, string Kind, string Label, bool IsDefault, string? DefaultModel, IReadOnlyList<string> Models);
