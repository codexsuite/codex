namespace Codex.Contracts.Prompts;

// `BuiltIn` templates ship with the app (read-only, duplicable); the rest are the signed-in user's own.
public sealed record PromptTemplateDto(string Id, string Name, string Description, bool BuiltIn);
