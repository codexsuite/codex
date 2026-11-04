namespace Codex.Contracts.Print;

// POST /api/layout-template-versions/{id}/fork: Name defaults to "<source name> (fork)".
public sealed record ForkTemplateRequest(string? Name = null);
