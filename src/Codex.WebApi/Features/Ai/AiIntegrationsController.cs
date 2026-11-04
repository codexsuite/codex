using Codex.Application.Features.Ai.Integrations;
using Codex.Contracts.Ai.Integrations;
using Codex.Contracts.Errors;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Ai;

// The signed-in user's AI integrations (#36, docs/ai-integrations.md). The key is write-only: no answer carries it.
// GET    /api/ai/integrations              -> AiSettingsDto (default first)
// POST   /api/ai/integrations              CreateAiIntegrationRequest -> AiIntegrationDto (the first one becomes the default)
// PATCH  /api/ai/integrations/{id}         UpdateAiIntegrationRequest -> AiIntegrationDto
// DELETE /api/ai/integrations/{id}         -> 204 (deleting the default promotes the oldest remaining one)
// POST   /api/ai/integrations/{id}/default -> AiSettingsDto
// POST   /api/ai/integrations/test         TestAiConnectionRequest -> AiConnectionTestDto
// POST   /api/ai/integrations/{id}/test    -> AiConnectionTestDto (caches the models, sets or clears last_error)
// GET    /api/ai/models                    -> AiModelChoicesDto (the Write draft panel's picker)
// Another user's integration 404; a bad kind, label, key or base URL (https and public addresses only) 400.
[Route("api/ai")]
public sealed class AiIntegrationsController(AiIntegrationQueries queries, AiIntegrationCommands commands) : ApiControllerBase
{
    [HttpGet("integrations")]
    [ProducesResponseType<AiSettingsDto>(StatusCodes.Status200OK)]
    public Task<AiSettingsDto> List(CancellationToken ct) => queries.ListAsync(ct);

    [HttpPost("integrations")]
    [ProducesResponseType<AiIntegrationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public Task<AiIntegrationDto> Create(CreateAiIntegrationRequest request, CancellationToken ct) => commands.CreateAsync(request, ct);

    [HttpPost("integrations/test")]
    [ProducesResponseType<AiConnectionTestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public Task<AiConnectionTestDto> Test(TestAiConnectionRequest request, CancellationToken ct) => commands.TestAsync(request, ct);

    [HttpPatch("integrations/{integrationId:guid}")]
    [ProducesResponseType<AiIntegrationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<AiIntegrationDto> Update(Guid integrationId, UpdateAiIntegrationRequest request, CancellationToken ct)
        => commands.UpdateAsync(integrationId, request, ct);

    [HttpDelete("integrations/{integrationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> Delete(Guid integrationId, CancellationToken ct) => NoContentAfter(commands.DeleteAsync(integrationId, ct));

    [HttpPost("integrations/{integrationId:guid}/default")]
    [ProducesResponseType<AiSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<AiSettingsDto> MakeDefault(Guid integrationId, CancellationToken ct) => commands.MakeDefaultAsync(integrationId, ct);

    [HttpPost("integrations/{integrationId:guid}/test")]
    [ProducesResponseType<AiConnectionTestDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<AiConnectionTestDto> TestSaved(Guid integrationId, CancellationToken ct) => commands.TestSavedAsync(integrationId, ct);

    [HttpGet("models")]
    [ProducesResponseType<AiModelChoicesDto>(StatusCodes.Status200OK)]
    public Task<AiModelChoicesDto> Models(CancellationToken ct) => queries.ModelsAsync(ct);
}
