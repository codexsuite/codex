using Codex.Application.Features.Structure;
using Codex.Contracts.Errors;
using Codex.Contracts.Structure;
using Codex.WebApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Codex.WebApi.Features.Structure;

// Plan board writes: create, rename, reorder and archive.
// POST  /api/books/{bookId:guid}/acts                -> 201 OutlineActDto (Location /api/acts/{id})
// PATCH /api/acts/{actId:guid}                       -> OutlineActDto
// POST  /api/acts/{actId:guid}/archive               -> ArchiveActResult
// POST  /api/acts/{actId:guid}/restore               -> OutlineActDto
// POST  /api/acts/{actId:guid}/chapters              -> 201 OutlineChapterDto (Location /api/chapters/{id})
// POST  /api/chapters/{chapterId:guid}/scenes        -> 201 OutlineSceneDto (Location /api/scenes/{id})
// PATCH /api/chapters/{chapterId:guid}               -> OutlineChapterDto
// POST  /api/chapters/{chapterId:guid}/archive       -> ArchiveChapterResult
// POST  /api/chapters/{chapterId:guid}/restore       -> OutlineChapterDto
// POST  /api/scenes/{sceneId:guid}/merge-previous    -> MergeSceneResult
// PUT   /api/acts/{actId:guid}/position              -> OutlineDto
// PUT   /api/chapters/{chapterId:guid}/position      -> OutlineDto
// PUT   /api/scenes/{sceneId:guid}/position          -> OutlineDto
// POST  /api/scenes/{sceneId:guid}/archive           -> ArchiveSceneResult
// POST  /api/scenes/{sceneId:guid}/restore           -> RestoreSceneResult
// GET   /api/chapters/{chapterId:guid}/archived-scenes -> ArchivedSceneDto[]
[Route("api")]
public sealed class PlanController(
    ActCommands acts,
    StructureCreateCommands create,
    ChapterCommands chapters,
    ChapterArchiveCommands chapterArchive,
    SceneMergeCommands merge,
    PositionCommands positions,
    SceneArchiveCommands sceneArchive) : ApiControllerBase
{
    [HttpPost("books/{bookId:guid}/acts")]
    [ProducesResponseType<OutlineActDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> CreateAct(Guid bookId, CreateActRequest request, CancellationToken ct)
    {
        var act = await acts.CreateAsync(bookId, request, ct);
        return Created($"/api/acts/{act.Id}", act);
    }

    [HttpPatch("acts/{actId:guid}")]
    [ProducesResponseType<OutlineActDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OutlineActDto> RenameAct(Guid actId, PatchActRequest request, CancellationToken ct) => acts.RenameAsync(actId, request, ct);

    [HttpPost("acts/{actId:guid}/archive")]
    [ProducesResponseType<ArchiveActResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<ArchiveActResult> ArchiveAct(Guid actId, CancellationToken ct) => acts.ArchiveAsync(actId, ct);

    [HttpPost("acts/{actId:guid}/restore")]
    [ProducesResponseType<OutlineActDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<OutlineActDto> RestoreAct(Guid actId, CancellationToken ct) => acts.RestoreAsync(actId, ct);

    [HttpPost("acts/{actId:guid}/chapters")]
    [ProducesResponseType<OutlineChapterDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> CreateChapter(Guid actId, CreateChapterRequest request, CancellationToken ct)
    {
        var chapter = await create.CreateChapterAsync(actId, request, ct);
        return Created($"/api/chapters/{chapter.Id}", chapter);
    }

    [HttpPost("chapters/{chapterId:guid}/scenes")]
    [ProducesResponseType<OutlineSceneDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<CreatedResult> CreateScene(Guid chapterId, CreateSceneRequest request, CancellationToken ct)
    {
        var scene = await create.CreateSceneAsync(chapterId, request, ct);
        return Created($"/api/scenes/{scene.Id}", scene);
    }

    [HttpPatch("chapters/{chapterId:guid}")]
    [ProducesResponseType<OutlineChapterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OutlineChapterDto> PatchChapter(Guid chapterId, PatchChapterRequest request, CancellationToken ct)
        => chapters.PatchAsync(chapterId, request, ct);

    [HttpPost("chapters/{chapterId:guid}/archive")]
    [ProducesResponseType<ArchiveChapterResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<ArchiveChapterResult> ArchiveChapter(Guid chapterId, CancellationToken ct) => chapterArchive.ArchiveAsync(chapterId, ct);

    [HttpPost("chapters/{chapterId:guid}/restore")]
    [ProducesResponseType<OutlineChapterDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<OutlineChapterDto> RestoreChapter(Guid chapterId, CancellationToken ct) => chapterArchive.RestoreAsync(chapterId, ct);

    [HttpPost("scenes/{sceneId:guid}/merge-previous")]
    [ProducesResponseType<MergeSceneResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status422UnprocessableEntity)]
    public Task<MergeSceneResult> MergeIntoPrevious(Guid sceneId, CancellationToken ct) => merge.MergeIntoPreviousAsync(sceneId, ct);

    [HttpPut("acts/{actId:guid}/position")]
    [ProducesResponseType<OutlineDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OutlineDto> MoveAct(Guid actId, MoveActRequest request, CancellationToken ct) => positions.MoveActAsync(actId, request, ct);

    [HttpPut("chapters/{chapterId:guid}/position")]
    [ProducesResponseType<OutlineDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OutlineDto> MoveChapter(Guid chapterId, MoveChapterRequest request, CancellationToken ct)
        => positions.MoveChapterAsync(chapterId, request, ct);

    [HttpPut("scenes/{sceneId:guid}/position")]
    [ProducesResponseType<OutlineDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<OutlineDto> MoveScene(Guid sceneId, MoveSceneRequest request, CancellationToken ct)
        => positions.MoveSceneAsync(sceneId, request, ct);

    [HttpPost("scenes/{sceneId:guid}/archive")]
    [ProducesResponseType<ArchiveSceneResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<ArchiveSceneResult> ArchiveScene(Guid sceneId, CancellationToken ct) => sceneArchive.ArchiveAsync(sceneId, ct);

    [HttpPost("scenes/{sceneId:guid}/restore")]
    [ProducesResponseType<RestoreSceneResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public Task<RestoreSceneResult> RestoreScene(Guid sceneId, CancellationToken ct) => sceneArchive.RestoreAsync(sceneId, ct);

    [HttpGet("chapters/{chapterId:guid}/archived-scenes")]
    [ProducesResponseType<IReadOnlyList<ArchivedSceneDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<ArchivedSceneDto>> ArchivedScenes(Guid chapterId, CancellationToken ct) => sceneArchive.ListAsync(chapterId, ct);
}
