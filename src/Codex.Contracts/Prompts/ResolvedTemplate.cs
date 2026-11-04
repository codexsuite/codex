namespace Codex.Contracts.Prompts;

// A template chosen for a draft. A built-in one is only an instruction, put in front of the standard prompt; a user's
// one is the whole prompt (`Text` with its variables, see PromptVariables), so it replaces the standard prompt.
public sealed record ResolvedTemplate(string Id, string Name, string Text, bool BuiltIn);
