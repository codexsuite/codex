namespace Codex.Contracts.Prompts;

// The templates the signed-in user can draft with: the built-in ones (IPromptCatalogue) and their own (Settings -> AI).
public interface IPromptTemplates
{
    // A built-in id or the id of one of the user's own templates; anything else (another user's too) is a 400 validation.
    Task<ResolvedTemplate> ResolveAsync(string templateId, CancellationToken cancellationToken);
}
