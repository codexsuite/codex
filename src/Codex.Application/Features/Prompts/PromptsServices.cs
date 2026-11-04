using Codex.Contracts;
using Codex.Contracts.Prompts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Prompts;

public sealed class PromptsServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPromptCatalogue, PromptCatalogue>();
        // GET /api/prompts uses UserPrompts; the drafting feature resolves a template (built-in or the user's own) through
        // the contract, so it never references this feature.
        services.AddSingleton<UserPrompts>();
        services.AddSingleton<IPromptTemplates>(sp => sp.GetRequiredService<UserPrompts>());
    }
}
