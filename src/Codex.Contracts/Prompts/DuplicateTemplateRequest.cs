namespace Codex.Contracts.Prompts;

// POST /api/ai/templates/{id}/duplicate: copies a built-in or the user's own template under a new name.
public sealed record DuplicateTemplateRequest(string Name);
