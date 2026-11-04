using Codex.Application.Features.Shell;
using Codex.Contracts.Books;
using Codex.Contracts.Errors;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Shell;

// GET /api/library?q=&sort=lastEdited|title|series|progress&group=series|status|none&status=<status|archived>
//   group is an ordering hint (DTO shape is always series + standalone): "status" orders standalone books by
//   lifecycle stage; "series" and "none" leave the order to sort. Unknown sort, group or status -> 400 validation.
//   Lists the caller's own books and those shared with them (#80), each with its role and owner.
// GET /api/books/{id:guid} -> BookHeaderDto (view+, with the caller's role). Unknown or not shared -> 404 not_found. A malformed id matches no route,
//   so the framework fallback answers 404 not_found too. An archived book is still 200.
[Route("api")]
public sealed class ShellController(IShellQueries queries) : ApiControllerBase
{
    [HttpGet("library")]
    [ProducesResponseType<LibraryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    public Task<LibraryDto> Library(string? q, string? sort, string? group, string? status, CancellationToken ct)
        => queries.GetLibraryAsync(
            new LibraryQuery(
                q,
                Checked(sort, LibrarySorts.All, LibrarySorts.LastEdited, "sort"),
                Checked(group, LibraryGroups.All, LibraryGroups.Series, "group"),
                string.IsNullOrEmpty(status) ? null : Checked(status, LibraryStatuses.All, null, "status")),
            ct);

    [HttpGet("books/{id:guid}")]
    [ProducesResponseType<BookHeaderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<BookHeaderDto> BookHeader(Guid id, CancellationToken ct)
        => queries.GetBookHeaderAsync(id, ct);

    private static string Checked(string? value, IReadOnlyList<string> allowed, string? fallback, string name)
    {
        if (string.IsNullOrEmpty(value))
        {
            return fallback!;
        }
        return allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : throw ApiException.Validation($"Unknown {name} '{value}'. Use one of: {string.Join(", ", allowed)}.");
    }
}
