using Codex.Contracts;
using Codex.Contracts.Manuscript;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Manuscript;

// ProseStore is the one IProseStore.
public sealed class ManuscriptServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IProseStore, ProseStore>();
    }
}
