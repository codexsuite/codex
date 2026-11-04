using Codex.Application.Features.Sharing;
using Codex.Contracts.Errors;
using Codex.Contracts.Sharing;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Sharing;

// Book settings -> Sharing (#80, docs/auth.md -> The access rule).
// GET    /api/books/{id}/sharing -> SharingDto (view+): the owner first, then the book's and the series' grants by name.
//   Emails are null unless the caller is the owner. A series id or an unknown book -> 404.
// POST   /api/books/{id}/sharing/lookup { email } -> SharingUserDto (owner): one exact, case-insensitive address. No such
//   (or a disabled) user -> 404; blank -> 400; over 20 a minute or 200 a day per caller -> 429 rate_limited.
// PUT    /api/books/{id}/sharing/{userId} { role } -> SharingGrantDto (owner): adds or changes a grant; id may be the
//   book's series. The owner or an archived book -> 409; unknown or disabled user -> 404; a bad role -> 400.
// DELETE /api/books/{id}/sharing/{userId} -> 204 (owner): removes the grant on id; none there -> 404.
[Route("api/books")]
public sealed class SharingController(SharingCommands sharing) : ApiControllerBase
{
    [HttpGet("{bookId:guid}/sharing")]
    [ProducesResponseType<SharingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<SharingDto> Get(Guid bookId, CancellationToken ct)
        => sharing.GetAsync(bookId, ct);

    [HttpPost("{bookId:guid}/sharing/lookup")]
    [ProducesResponseType<SharingUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status429TooManyRequests)]
    public Task<SharingUserDto> Lookup(Guid bookId, SharingLookupRequest request, CancellationToken ct)
        => sharing.LookupAsync(bookId, request, ct);

    [HttpPut("{bookId:guid}/sharing/{userId:guid}")]
    [ProducesResponseType<SharingGrantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SharingGrantDto> SetGrant(Guid bookId, Guid userId, SetGrantRequest request, CancellationToken ct)
        => sharing.SetAsync(bookId, userId, request, ct);

    [HttpDelete("{bookId:guid}/sharing/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> RemoveGrant(Guid bookId, Guid userId, CancellationToken ct)
        => NoContentAfter(sharing.RemoveAsync(bookId, userId, ct));
}
