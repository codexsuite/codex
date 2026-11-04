using Codex.Application.Features.Print;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// PUT /api/asset-uploads/{token}   the raw image bytes (curl -T) -> 201 AssetDto. The link create_image_upload made over
//                                  /mcp (#65) is the credential, so the route takes no cookie. 404 an unknown, used or
//                                  expired link; 400 a declared length over 25 MB or other than the link's, or not an image.
[Route("api/asset-uploads")]
public sealed class AssetUploadsController(IAssetUploadLinks links) : ApiControllerBase
{
    [HttpPut("{token}")]
    [AllowAnonymous]
    [ProducesResponseType<AssetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> Upload(string token, CancellationToken ct)
    {
        var asset = await links.UploadAsync(token, Request.ContentLength, Request.Body, ct);
        return Created($"/api/assets/{asset.Id}/info", asset);
    }
}
