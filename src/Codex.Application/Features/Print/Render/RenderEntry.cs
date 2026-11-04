namespace Codex.Application.Features.Print.Render;

// A knowledge-base entry placed with ::spell or ::statblock. Fields is the entry's fields JSON object.
public sealed record RenderEntry(Guid Id, string Name, string Fields, string Body);
