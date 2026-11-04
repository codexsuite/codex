using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Codex.Application.Features.Print.Render;

// RenderSettings is read from configuration when first resolved, so test hosts can override it after registration.
public sealed class RenderServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp => RenderSettings.From(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
        services.AddHostedService<RenderWorkSweeper>();
    }
}
