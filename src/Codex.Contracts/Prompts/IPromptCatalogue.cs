namespace Codex.Contracts.Prompts;

// The prompt templates and models shipped as files under prompts/.
public interface IPromptCatalogue
{
    PromptsDto Get();

    // The template's instruction text, or null for an unknown id.
    string? FindInstruction(string templateId);
}
