using Codex.Contracts;
using Codex.Contracts.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Ai;

// AI drafting runs on the current user's own integration (#36, docs/ai-integrations.md): there is no server-wide key,
// and nothing in the environment turns drafting on. A user with no integration gets `not_configured`.
public sealed class AiServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(AnthropicProseGenerator.HttpClientName);
        services.AddSingleton<IProseGenerator, IntegrationProseGenerator>();
    }
}
