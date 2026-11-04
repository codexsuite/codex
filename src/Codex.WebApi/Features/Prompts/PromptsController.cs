using Codex.Application.Features.Prompts;
using Codex.Contracts.Errors;
using Codex.Contracts.Prompts;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Prompts;

// GET /api/prompts -> PromptsDto { templates: [{id,name,description,builtIn}], models: [{id,label,default}] }: the
// built-in templates (files under prompts/) plus the signed-in user's own; the models are the catalogue's label
// suggestions (the models a draft can use come from the user's AI integrations, GET /api/ai/models).
//
// Settings -> AI, Prompt templates (J22), all the signed-in user's own:
// GET    /api/ai/templates                 -> AiTemplatesDto (built-in and own templates with their text, the variables)
// POST   /api/ai/templates                 SaveTemplateRequest -> AiTemplateDto;  PUT/DELETE /api/ai/templates/{id}
// POST   /api/ai/templates/{id}/duplicate  DuplicateTemplateRequest -> AiTemplateDto (a built-in or the user's own)
// A built-in template cannot be changed (400); an id that is not the caller's is 404.
[Route("api")]
public sealed class PromptsController(UserPrompts prompts, PromptTemplateCommands templates) : ApiControllerBase
{
    [HttpGet("prompts")]
    [ProducesResponseType<PromptsDto>(StatusCodes.Status200OK)]
    public Task<PromptsDto> Get(CancellationToken ct) => prompts.GetAsync(ct);

    [HttpGet("ai/templates")]
    [ProducesResponseType<AiTemplatesDto>(StatusCodes.Status200OK)]
    public Task<AiTemplatesDto> ListTemplates(CancellationToken ct) => templates.GetAsync(ct);

    [HttpPost("ai/templates")]
    [ProducesResponseType<AiTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<AiTemplateDto> CreateTemplate(SaveTemplateRequest request, CancellationToken ct) => templates.CreateTemplateAsync(request, ct);

    [HttpPut("ai/templates/{templateId}")]
    [ProducesResponseType<AiTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<AiTemplateDto> UpdateTemplate(string templateId, SaveTemplateRequest request, CancellationToken ct)
        => templates.UpdateTemplateAsync(templateId, request, ct);

    [HttpDelete("ai/templates/{templateId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> DeleteTemplate(string templateId, CancellationToken ct)
        => NoContentAfter(templates.DeleteTemplateAsync(templateId, ct));

    [HttpPost("ai/templates/{templateId}/duplicate")]
    [ProducesResponseType<AiTemplateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<AiTemplateDto> DuplicateTemplate(string templateId, DuplicateTemplateRequest request, CancellationToken ct)
        => templates.DuplicateTemplateAsync(templateId, request, ct);
}
