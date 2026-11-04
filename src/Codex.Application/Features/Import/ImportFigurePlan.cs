namespace Codex.Application.Features.Import;

// A figure the scene's prose places with its ::figure{id="Id"} line: the image's path in the upload (an ImportImage),
// a figure.placement value and the caption.
public sealed record ImportFigurePlan(Guid Id, string ImagePath, string Placement, string? Caption);
