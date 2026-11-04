namespace Codex.Application.Features.Print.Render;

public sealed record RenderAct(Guid Id, string Title, Guid? OpenerAssetId, string? OpenerCaption);
