using System.Text.Json;
using Codex.Contracts.Ai.Integrations;
using Codex.Core;

namespace Codex.Application.Features.Ai.Integrations;

// One ai_integration row as the DTO needs it. It has no credential member on purpose: nothing that is serialised can
// carry the key. Models arrives as JSON because Dapper can't bind a text[] to a record.
internal sealed record AiIntegrationRow(
    Guid Id, string Kind, string Label, string? BaseUrl, string KeyHint, string? DefaultModel, string ModelsJson, bool IsDefault,
    DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, string? LastError)
{
    internal const string Columns =
        """
        id AS Id, kind AS Kind, label AS Label, base_url AS BaseUrl, key_hint AS KeyHint, default_model AS DefaultModel,
        to_json(models)::text AS ModelsJson, is_default AS IsDefault, created_at AS CreatedAt, last_used_at AS LastUsedAt,
        last_error AS LastError
        """;

    private IReadOnlyList<string> Models => JsonSerializer.Deserialize<string[]>(ModelsJson) ?? [];

    public AiIntegrationDto ToDto(TimeProvider clock) => new(
        Id, Kind, Label, BaseUrl, KeyHint, DefaultModel, Models, IsDefault,
        DisplayDate.Relative(CreatedAt, clock), LastUsedAt is { } used ? DisplayDate.Relative(used, clock) : null, LastError);

    public AiModelChoiceDto ToChoice() => new(Id, Kind, Label, IsDefault, DefaultModel, Models);
}
