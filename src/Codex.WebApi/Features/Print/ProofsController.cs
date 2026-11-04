using Codex.Application.Features.Print.Proof;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Print;

// POST /api/books/{bookId}/proofs                    ProofRequest -> ProofDto: queues a proof of one chapter (202), or answers with the cached
//                                                    or already running proof of the same content, template version, trim and options (200, Cached).
//                                                    404 unknown book or chapter; 400 a bad trim or option
// GET  /api/books/{bookId}/proofs?chapterId=         -> ProofListDto: the chapter's proofs, newest first, and the cache key of the chapter as it is now. 400 no chapterId
// GET  /api/proofs/{id}                              -> ProofDto with its log. 404 unknown proof
// GET  /api/proofs/{id}/pdf                          -> the PDF. 404 unknown proof; 409 not finished or failed
// The proof is compiled by the worker (CODEX_ROLE worker or all), so a web-only host queues it and never renders.
[Route("api")]
public sealed class ProofsController(IProofService proofs) : ApiControllerBase
{
    [HttpPost("books/{bookId:guid}/proofs")]
    [ProducesResponseType<ProofDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProofDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProofDto>> Queue(Guid bookId, ProofRequest request, CancellationToken ct)
    {
        var proof = await proofs.RequestAsync(bookId, request, ct);
        return proof.Cached ? Ok(proof) : Accepted($"/api/proofs/{proof.Id}", proof);
    }

    [HttpGet("books/{bookId:guid}/proofs")]
    [ProducesResponseType<ProofListDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ProofListDto> List(Guid bookId, Guid? chapterId, CancellationToken ct)
        => proofs.ListAsync(bookId, chapterId ?? throw ApiException.Validation("Name the chapter whose proofs to list (?chapterId=)."), ct);

    [HttpGet("proofs/{id:guid}")]
    [ProducesResponseType<ProofDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<ProofDto> Get(Guid id, CancellationToken ct) => proofs.GetAsync(id, ct);

    [HttpGet("proofs/{id:guid}/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<FileStreamResult> Pdf(Guid id, CancellationToken ct)
    {
        var file = await proofs.OpenPdfAsync(id, ct);
        return File(file.Content, file.ContentType);
    }
}
