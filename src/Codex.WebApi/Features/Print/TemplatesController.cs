using Codex.Application.Features.Print.Layout;
using Codex.Application.Features.Print.Templates;
using Codex.Contracts.Errors;
using Codex.Contracts.Print;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Codex.WebApi.Features.Print;

// Template management (issue #13 phase 6). Another user's template or version is 404; built-ins can be forked,
// downloaded and previewed by anyone, never changed.
// POST /api/layout-templates                                 multipart: file (.zip, 25 MB) -> 201 LayoutTemplateVersionDto: a new template of
//                                                            the user's. 400 not a zip, unsafe zip or a package with problems; 409 slug taken
// POST /api/layout-templates/{templateId}/versions           multipart: file -> 201 LayoutTemplateVersionDto: a new version of the user's
//                                                            template. 404 unknown; 400 built-in, other slug, not newer or problems
// POST /api/layout-templates/{templateId}/archive            -> 204: hidden from the picker. 404 unknown; 400 built-in; 409 a book uses it
// POST /api/layout-template-versions/{versionId}/fork        ForkTemplateRequest -> 201 LayoutTemplateVersionDto: a copy at 1.0.0. 404 unknown
// GET  /api/layout-template-versions/{versionId}/package     the package .zip (attachment). 404 unknown
// GET  /api/layout-template-versions/{versionId}/sample      -> LayoutTemplateSampleDto: sample.md as html5 for the thumbnail. 404 unknown; 503 no pandoc
// POST /api/books/{bookId}/layout/upgrade                    LayoutUpgradeRequest -> LayoutStateDto: re-pins the book's template version. 404 unknown book
//                                                            or version, or no template chosen; 400 a version of another template
[Route("api")]
public sealed class TemplatesController(ITemplateLibrary library, ILayoutService layout) : ApiControllerBase
{
    [HttpPost("layout-templates")]
    [ProducesResponseType<LayoutTemplateVersionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<CreatedResult> Upload(CancellationToken ct)
    {
        var created = await library.UploadAsync(null, token => TemplatePackageReader.ReadAsync(Request, token), ct);
        return Created($"/api/layout-template-versions/{created.VersionId}/package", created);
    }

    [HttpPost("layout-templates/{templateId:guid}/versions")]
    [ProducesResponseType<LayoutTemplateVersionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> UploadVersion(Guid templateId, CancellationToken ct)
    {
        var created = await library.UploadAsync(templateId, token => TemplatePackageReader.ReadAsync(Request, token), ct);
        return Created($"/api/layout-template-versions/{created.VersionId}/package", created);
    }

    [HttpPost("layout-templates/{templateId:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<NoContentResult> Archive(Guid templateId, CancellationToken ct) => NoContentAfter(library.ArchiveAsync(templateId, ct));

    [HttpPost("layout-template-versions/{versionId:guid}/fork")]
    [ProducesResponseType<LayoutTemplateVersionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> Fork(Guid versionId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ForkTemplateRequest? request, CancellationToken ct)
    {
        var created = await library.ForkAsync(versionId, request ?? new ForkTemplateRequest(), ct);
        return Created($"/api/layout-template-versions/{created.VersionId}/package", created);
    }

    [HttpGet("layout-template-versions/{versionId:guid}/package")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<FileStreamResult> Package(Guid versionId, CancellationToken ct)
    {
        var (content, fileName) = await library.DownloadAsync(versionId, ct);
        return File(content, "application/zip", fileName);
    }

    [HttpGet("layout-template-versions/{versionId:guid}/sample")]
    [ProducesResponseType<LayoutTemplateSampleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public Task<LayoutTemplateSampleDto> Sample(Guid versionId, CancellationToken ct) => library.SampleAsync(versionId, ct);

    [HttpPost("books/{bookId:guid}/layout/upgrade")]
    [ProducesResponseType<LayoutStateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<LayoutStateDto> Upgrade(Guid bookId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LayoutUpgradeRequest? request, CancellationToken ct)
        => layout.UpgradeAsync(bookId, request ?? new LayoutUpgradeRequest(), ct);
}
