using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.References;

public sealed class QuoteFlagServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<QuoteFlagScanner>();
        services.AddHostedService(sp => sp.GetRequiredService<QuoteFlagScanner>());
    }
}
