namespace Codex.Application.Features.Import;

// Opener art for an act or chapter: the image's path in the upload (an ImportImage) and its caption.
public sealed record ImportOpenerPlan(string ImagePath, string? Caption);
