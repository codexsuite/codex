using Codex.Application.Features.Print;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// POST /api/books/{bookId}/assets            multipart: file (PNG, JPEG or WebP, 25 MB), source?, prompt?, licence? -> 201 AssetDto
//                                            (status "processing" until the worker has made web, proxy and print). 404 unknown book;
//                                            400 no file, empty, oversize, unsupported type or not an image.
// GET  /api/books/{bookId}/assets            ?trim=&columns=&placement=&target= -> AssetDto[] (the book's and its series' images); a trim adds Dpi
// GET  /api/assets/{id}/info                 same query -> AssetDto. 404 unknown
// GET  /api/assets/{id}                      the original file, inline
// GET  /api/assets/{id}/renditions/{purpose} web | proxy | print file. 404 until the worker has made it
// GET  /api/assets/{id}/dpi                  ?trim=(required)&columns=1&placement=column&target=300 -> AssetDpiDto. 400 unknown trim/placement
// POST /api/assets/{id}/renditions           queue the renditions again -> AssetDto. 409 while queued or running
[Route("api")]
public sealed class AssetsController(IAssetService assets) : ApiControllerBase
{
    [HttpPost("books/{bookId:guid}/assets")]
    [ProducesResponseType<AssetDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> Upload(Guid bookId, CancellationToken ct)
    {
        var asset = await AssetFormReader.UploadAsync(assets, bookId, Request, ct);
        return Created($"/api/assets/{asset.Id}/info", asset);
    }

    [HttpGet("books/{bookId:guid}/assets")]
    [ProducesResponseType<IReadOnlyList<AssetDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<AssetDto>> List(Guid bookId, string? trim, int? columns, string? placement, int? target, CancellationToken ct)
        => assets.ListAsync(bookId, DpiRequest(trim, columns, placement, target), ct);

    [HttpGet("assets/{id:guid}/info")]
    [ProducesResponseType<AssetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<AssetDto> Info(Guid id, string? trim, int? columns, string? placement, int? target, CancellationToken ct)
        => assets.GetAsync(id, DpiRequest(trim, columns, placement, target), ct);

    [HttpGet("assets/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<FileStreamResult> Original(Guid id, CancellationToken ct)
    {
        var file = await assets.OpenAsync(id, null, ct);
        return File(file.Content, file.ContentType);
    }

    [HttpGet("assets/{id:guid}/renditions/{purpose}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<FileStreamResult> Rendition(Guid id, string purpose, CancellationToken ct)
    {
        var file = await assets.OpenAsync(id, purpose, ct);
        return File(file.Content, file.ContentType);
    }

    [HttpGet("assets/{id:guid}/dpi")]
    [ProducesResponseType<AssetDpiDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<AssetDpiDto> ImageDpi(Guid id, string? trim, int? columns, string? placement, int? target, CancellationToken ct)
        => assets.DpiAsync(id, DpiRequest(trim, columns, placement, target)
            ?? throw ApiException.Validation("Name a trim size, e.g. ?trim=6x9."), ct);

    [HttpPost("assets/{id:guid}/renditions")]
    [ProducesResponseType<AssetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<AssetDto> Requeue(Guid id, CancellationToken ct) => assets.RequeueAsync(id, ct);

    private static AssetDpiRequest? DpiRequest(string? trim, int? columns, string? placement, int? target)
    {
        if (string.IsNullOrWhiteSpace(trim))
        {
            return null;
        }

        var kind = string.IsNullOrWhiteSpace(placement) ? "column" : placement;
        return new AssetDpiRequest(trim, columns ?? 1, kind, target ?? EffectiveDpi.PrintTarget);
    }
}
