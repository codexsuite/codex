using Codex.Application.Features.Print;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// PUT /api/acts/{id}/opener       SetOpenerRequest -> OpenerDto: the act's part-opener art (assetId null clears it)
// PUT /api/chapters/{id}/opener   SetOpenerRequest -> OpenerDto: the chapter's opener art
// 404 unknown or archived act or chapter, or unknown image; 400 an image outside the book and its series.
[Route("api")]
public sealed class OpenersController(OpenerCommands commands) : ApiControllerBase
{
    [HttpPut("acts/{actId:guid}/opener")]
    [ProducesResponseType<OpenerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OpenerDto> SetAct(Guid actId, SetOpenerRequest request, CancellationToken ct) => commands.SetActAsync(actId, request, ct);

    [HttpPut("chapters/{chapterId:guid}/opener")]
    [ProducesResponseType<OpenerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OpenerDto> SetChapter(Guid chapterId, SetOpenerRequest request, CancellationToken ct) => commands.SetChapterAsync(chapterId, request, ct);
}
