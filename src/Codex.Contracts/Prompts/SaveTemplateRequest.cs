namespace Codex.Contracts.Prompts;

// POST /api/ai/templates and PUT /api/ai/templates/{id}. The name is unique among the user's templates (and differs
// from the built-in ones); the body may not be empty.
public sealed record SaveTemplateRequest(string Name, string? Description, string Body);
