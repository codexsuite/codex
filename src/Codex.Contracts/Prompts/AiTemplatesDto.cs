namespace Codex.Contracts.Prompts;

// GET /api/ai/templates: the template section of Settings -> AI in one request, built-in templates first. `Variables`
// are the names a template body may use.
public sealed record AiTemplatesDto(IReadOnlyList<AiTemplateDto> Templates, IReadOnlyList<string> Variables);
