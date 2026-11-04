using System.Globalization;
using Codex.Application.Features.References;
using Codex.Contracts.Errors;
using Codex.Contracts.References;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Codex.WebApi.Features.References;

// POST  /api/references/{id}/permission           -> SourceDetailDto with a drafted request. 409 while one is draft, sent or granted.
// PATCH /api/permission-requests/{id}             {body?, to?, remindOn?} -> SourceDetailDto. 400 for a blank letter or bad date; 409 when the status does not allow it.
// POST  /api/permission-requests/{id}/sent        -> SourceDetailDto. 409 unless a draft.
// POST  /api/permission-requests/{id}/granted     multipart: letter (file), creditLine?, terms?, fee? -> SourceDetailDto. 400 for a negative or unreadable fee; 400 without a file, an empty, oversize (10 MB) or disallowed one; 409 unless sent.
// POST  /api/permission-requests/{id}/refused     {note?} (body optional) -> SourceDetailDto. 409 unless sent.
// POST  /api/permission-requests/{id}/withdraw    -> SourceDetailDto. 409 unless draft or sent.
// POST  /api/permission-requests/{id}/followed-up -> SourceDetailDto. Stamps last_reminded_at. 409 unless sent.
// POST  /api/permission-requests/{id}/letter      multipart: letter (file) -> SourceDetailDto. Attaches a letter to a granted request that keeps none. 400 / 409.
// DELETE /api/permission-requests/{id}/letter     -> SourceDetailDto. Removes the kept letter. 404 when there is none; 409 unless granted.
// GET   /api/permission-requests/{id}/letter      -> the kept letter file. 404 when there is none.
[Route("api")]
public sealed class PermissionsController(IPermissionCommands commands) : ApiControllerBase
{
    [HttpPost("references/{id:guid}/permission")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SourceDetailDto> Draft(Guid id, CancellationToken ct) => commands.DraftAsync(id, ct);

    [HttpPatch("permission-requests/{id:guid}")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SourceDetailDto> Patch(Guid id, PatchPermissionRequest request, CancellationToken ct) => commands.PatchAsync(id, request, ct);

    [HttpPost("permission-requests/{id:guid}/sent")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SourceDetailDto> MarkSent(Guid id, CancellationToken ct) => commands.MarkSentAsync(id, ct);

    [HttpPost("permission-requests/{id:guid}/granted")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<SourceDetailDto> Grant(Guid id, CancellationToken ct)
    {
        if (!HasForm(Request))
        {
            throw ApiException.Validation("Send the grant as multipart form data with the signed letter.");
        }

        var form = await ReadFormAsync(Request, ct);
        var file = LetterFile(form);
        await using var content = file?.OpenReadStream();
        var upload = file is null || content is null ? null : new PermissionLetterUpload(file.FileName, file.ContentType, file.Length, content);
        decimal? fee = null;
        if (!string.IsNullOrWhiteSpace(form["fee"]))
        {
            fee = decimal.TryParse(form["fee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed < 100_000_000m
                ? parsed
                : throw ApiException.Validation("The fee must be a number.");
        }

        return await commands.GrantAsync(id, upload, form["creditLine"], form["terms"], ct, fee);
    }

    [HttpPost("permission-requests/{id:guid}/followed-up")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SourceDetailDto> MarkFollowedUp(Guid id, CancellationToken ct) => commands.MarkFollowedUpAsync(id, ct);

    [HttpPost("permission-requests/{id:guid}/letter")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<SourceDetailDto> AttachLetter(Guid id, CancellationToken ct)
    {
        if (!HasForm(Request))
        {
            throw ApiException.Validation("Send the letter as multipart form data.");
        }

        var form = await ReadFormAsync(Request, ct);
        var file = LetterFile(form);
        await using var content = file?.OpenReadStream();
        var upload = file is null || content is null ? null : new PermissionLetterUpload(file.FileName, file.ContentType, file.Length, content);
        return await commands.AttachLetterAsync(id, upload, ct);
    }

    [HttpDelete("permission-requests/{id:guid}/letter")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SourceDetailDto> RemoveLetter(Guid id, CancellationToken ct) => commands.RemoveLetterAsync(id, ct);

    // The body is optional. Routing's content-type check (AcceptsMatcherPolicy) ignores IsOptional and drops a
    // JSON-body endpoint for a request with no Content-Type, so the /api fallback answered 404. Accepting */*
    // lets a body-less POST through; a body of another type still gets 415 from the JSON binder.
    [HttpPost("permission-requests/{id:guid}/refused")]
    [AnyContentType]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SourceDetailDto> Refuse(Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefusePermissionRequest? request, CancellationToken ct)
        => commands.RefuseAsync(id, request ?? new RefusePermissionRequest(null), ct);

    [HttpPost("permission-requests/{id:guid}/withdraw")]
    [ProducesResponseType<SourceDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<SourceDetailDto> Withdraw(Guid id, CancellationToken ct) => commands.WithdrawAsync(id, ct);

    [HttpGet("permission-requests/{id:guid}/letter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<FileStreamResult> GetLetter(Guid id, CancellationToken ct)
    {
        var letter = await commands.GetLetterAsync(id, ct) ?? throw ApiException.NotFound($"Permission request {id} has no letter.");
        return File(letter.Content.Content, letter.Content.ContentType, letter.FileName);
    }

    // Uploads keep reading the request directly (no IFormFile binding), so the size limits and the 400s stay as they were.
    private static IFormFile? LetterFile(IFormCollection form) => form.Files["letter"];

    private static bool HasForm(HttpRequest request) => request.HasFormContentType;

    private static Task<IFormCollection> ReadFormAsync(HttpRequest request, CancellationToken ct) => request.ReadFormAsync(ct);

    // The minimal API's .Accepts<RefusePermissionRequest>(isOptional: true, "application/json", "*/*"). Not a
    // ConsumesAttribute: that refuses "*/*", and MVC turns it into a second, non-optional AcceptsMetadata for routing.
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class AnyContentTypeAttribute : Attribute, IAcceptsMetadata
    {
        public IReadOnlyList<string> ContentTypes { get; } = ["application/json", "*/*"];

        public Type? RequestType => typeof(RefusePermissionRequest);

        public bool IsOptional => true;
    }
}
