using Codex.Application.Features.KnowledgeBase;
using Codex.Contracts.Errors;
using Codex.Contracts.KnowledgeBase;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.KnowledgeBase;

// GET   /api/kb/entries/{id}                 -> KbEntryDto: the whole window in one response. Unknown or deleted entry 404.
// POST  /api/books/{id}/kb/entries           {categoryId, name} -> 201 KbEntryDto, last in its category. Unknown book 404;
//                                            blank name or a category outside the book and its series 400 validation.
// PATCH /api/kb/entries/{id}                 {name?, summary?, body?, fields?, customFields?, aliases?, alwaysInContext?,
//                                            categoryId?} -> KbEntryDto. An alias that is another entry's name or alias in
//                                            scope 409 conflict naming it; unknown field key or foreign category 400.
// PUT   /api/kb/entries/{id}/position        {afterId?} -> 204; null puts the entry first in its category.
// POST  /api/books/{id}/kb/categories        {name, colour} -> 201 KbCategoryDto. Blank name or unknown colour 400; a name
//                                            the book already shows 409.
// Every write publishes StructureChanged(book or series, "kb").
[Route("api")]
public sealed class KbEntriesController(IKbEntryQueries queries, IKbEntryCommands commands) : ApiControllerBase
{
    [HttpGet("kb/entries/{id:guid}")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<KbEntryDto> Get(Guid id, CancellationToken ct) => queries.GetAsync(id, ct);

    [HttpPost("books/{id:guid}/kb/entries")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> Create(Guid id, CreateKbEntryRequest request, CancellationToken ct)
    {
        var entry = await commands.CreateAsync(id, request, ct);
        return Created($"/api/kb/entries/{entry.Id}", entry);
    }

    [HttpPatch("kb/entries/{id:guid}")]
    [ProducesResponseType<KbEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<KbEntryDto> Patch(Guid id, PatchKbEntryRequest request, CancellationToken ct) => commands.PatchAsync(id, request, ct);

    [HttpPut("kb/entries/{id:guid}/position")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<NoContentResult> Move(Guid id, MoveKbEntryRequest request, CancellationToken ct) => NoContentAfter(commands.MoveAsync(id, request, ct));

    [HttpPost("books/{id:guid}/kb/categories")]
    [ProducesResponseType<KbCategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<CreatedResult> CreateCategory(Guid id, CreateKbCategoryRequest request, CancellationToken ct)
        => Created($"/api/books/{id}/kb", await commands.CreateCategoryAsync(id, request, ct));
}
