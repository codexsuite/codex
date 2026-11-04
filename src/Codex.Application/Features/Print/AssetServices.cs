using Codex.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codex.Application.Features.Print;

// The rendition worker is one hosted service in whichever process runs the app (web and worker images alike).
public sealed class AssetServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AssetRenditionWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<AssetRenditionWorker>());
    }
}
