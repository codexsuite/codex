namespace Codex.Contracts.Prompts;

// GET /api/prompts: the draft panel's template and model pickers.
public sealed record PromptsDto(IReadOnlyList<PromptTemplateDto> Templates, IReadOnlyList<PromptModelDto> Models);
