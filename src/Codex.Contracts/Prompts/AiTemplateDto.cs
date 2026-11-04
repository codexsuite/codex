namespace Codex.Contracts.Prompts;

// A template on Settings -> AI, with its text. A built-in one is read-only: its Body is the instruction text it sends.
public sealed record AiTemplateDto(string Id, string Name, string Description, string Body, bool BuiltIn);
