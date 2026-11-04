namespace Codex.Application.Features.Print.Render;

// A live figure row (the ::figure line owns where it sits; the row owns these fields).
public sealed record RenderFigure(Guid Id, Guid AssetId, string Placement, string? Caption, string? AltText);
